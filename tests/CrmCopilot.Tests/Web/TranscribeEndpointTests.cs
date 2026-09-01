using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Tests.Email.TestSupport;
using CrmCopilot.Tests.TestSupport;
using CrmCopilot.Tests.Web.TestSupport;
using CrmCopilot.Web;
using CrmCopilot.Web.Chat;
using CrmCopilot.Web.Speech;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// P0-15 (WP4) coverage for POST /api/transcribe. ITranscriber is DI-overridden throughout, so the
/// real Gemini call is never made — what is under test is the endpoint's validation ladder, its
/// contract, and its log hygiene.
///
/// Note what is deliberately NOT asserted here: that the transcript passes InputGuard. It does not,
/// and must not — this endpoint hands text back to the RM, and InputGuard runs later, on the
/// /api/chat path, once the RM presses Gửi (ChatOrchestrator.HandleAsync).
/// </summary>
public class TranscribeEndpointTests
{
    private const string WebmContentType = "audio/webm;codecs=opus";

    private static (WebApplicationFactory<WebEntryPoint> Factory, FakeTranscriber Transcriber, CapturingLogger<GeminiTranscriber> Logger) CreateFactory()
    {
        var transcriber = new FakeTranscriber();
        var logger = new CapturingLogger<GeminiTranscriber>();

        var factory = WebTestHost.CreateWithDefaults()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITranscriber>();
                services.AddSingleton<ITranscriber>(transcriber);

                // A closed ILogger<T> registration wins over the open-generic default, so the
                // endpoint's own audit line lands in this capture.
                services.RemoveAll<ILogger<GeminiTranscriber>>();
                services.AddSingleton<ILogger<GeminiTranscriber>>(logger);
            }));

        return (factory, transcriber, logger);
    }

    private static MultipartFormDataContent AudioForm(
        byte[] bytes, string contentType = WebmContentType, string fieldName = "audio")
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { part, fieldName, "recording.webm" } };
    }

    private static byte[] Audio(int byteCount = 2048) => new byte[byteCount];

    // --- 1. Not signed in -------------------------------------------------------------------
    [Fact]
    public async Task Transcribe_WithoutAuthentication_Returns401()
    {
        var (factory, transcriber, _) = CreateFactory();
        await using var factoryDisposable = factory;
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/transcribe", AudioForm(Audio()), TestContext.Current.CancellationToken);

        // A bare 401, not a 302 to the login page — the cookie handler special-cases /api paths so a
        // fetch() gets a status it can act on.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, transcriber.CallCount);
    }

    // --- 2. Oversized recording -------------------------------------------------------------
    [Fact]
    public async Task Transcribe_AudioOverOneMegabyte_Returns400WithoutCallingTheModel()
    {
        var (factory, transcriber, _) = CreateFactory();
        await using var factoryDisposable = factory;
        using var client = await ChatTestHarness.CreateAuthenticatedClientAsync(factory, TestContext.Current.CancellationToken);

        var oversized = Audio((int)TranscribeEndpoints.MaxAudioBytes + 1024);
        using var response = await client.PostAsync("/api/transcribe", AudioForm(oversized), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ChatTurnError>(TestContext.Current.CancellationToken);
        Assert.Equal(ChatTurnErrorCode.InvalidArgument, error!.Code);

        // The size check runs before the model call, so quota is never spent on a request that
        // was always going to be refused.
        Assert.Equal(0, transcriber.CallCount);
    }

    // --- 3. Wrong content type --------------------------------------------------------------
    [Fact]
    public async Task Transcribe_NonAudioContentType_Returns400WithoutCallingTheModel()
    {
        var (factory, transcriber, _) = CreateFactory();
        await using var factoryDisposable = factory;
        using var client = await ChatTestHarness.CreateAuthenticatedClientAsync(factory, TestContext.Current.CancellationToken);

        using var response = await client.PostAsync(
            "/api/transcribe", AudioForm(Audio(), contentType: "application/octet-stream"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ChatTurnError>(TestContext.Current.CancellationToken);
        Assert.Equal(ChatTurnErrorCode.InvalidArgument, error!.Code);
        Assert.Equal(0, transcriber.CallCount);
    }

    // --- 4. Happy path, normalized ----------------------------------------------------------
    [Fact]
    public async Task Transcribe_Success_ReturnsNormalizedTextAndStripsMimeParameters()
    {
        var (factory, transcriber, _) = CreateFactory();
        await using var factoryDisposable = factory;
        transcriber.Result = "Tìm hồ sơ khách hàng C U S 0 0 0 1";

        using var client = await ChatTestHarness.CreateAuthenticatedClientAsync(factory, TestContext.Current.CancellationToken);
        using var response = await client.PostAsync("/api/transcribe", AudioForm(Audio()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TranscribeResponse>(TestContext.Current.CancellationToken);

        // Spike A's spelled-out shape arrives normalized, which is what lets InputGuard recognise the
        // id when the RM later presses Gửi.
        Assert.Equal("Tìm hồ sơ khách hàng CUS-0001", body!.Text);

        // Part.FromBytes needs a bare IANA type, so ";codecs=opus" must be stripped before the call.
        Assert.Equal("audio/webm", transcriber.LastMimeType);
        Assert.Equal(1, transcriber.CallCount);
    }

    // --- 5. Model failure -------------------------------------------------------------------
    [Fact]
    public async Task Transcribe_ModelFailure_Returns502WithoutLeakingTheException()
    {
        var (factory, transcriber, _) = CreateFactory();
        await using var factoryDisposable = factory;
        transcriber.ThrowOnTranscribe = new ChatModelException(
            "Gemini transcribe call thất bại.", retryable: true, new InvalidOperationException("raw-sdk-detail-xyz"));

        using var client = await ChatTestHarness.CreateAuthenticatedClientAsync(factory, TestContext.Current.CancellationToken);
        using var response = await client.PostAsync("/api/transcribe", AudioForm(Audio()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("raw-sdk-detail-xyz", raw, StringComparison.Ordinal);

        var error = await response.Content.ReadFromJsonAsync<ChatTurnError>(TestContext.Current.CancellationToken);
        Assert.Equal(ChatTurnErrorCode.ModelError, error!.Code);
        Assert.True(error.Retryable);
    }

    // --- 6. Log hygiene ---------------------------------------------------------------------
    /// <summary>
    /// WP4: "Log chỉ ghi durationMs, bytes, status — KHÔNG ghi transcript." Scans rendered messages,
    /// structured state values and any captured exception (CapturingLogger.AllCapturedText) — a
    /// transcript could otherwise hide in a structured property that never renders into the message.
    /// </summary>
    [Fact]
    public async Task Transcribe_NeverLogsTheTranscriptItself()
    {
        const string sentinel = "SENTINEL-TRANSCRIPT-KHONG-DUOC-GHI-LOG";

        var (factory, transcriber, logger) = CreateFactory();
        await using var factoryDisposable = factory;
        transcriber.Result = sentinel;

        using var client = await ChatTestHarness.CreateAuthenticatedClientAsync(factory, TestContext.Current.CancellationToken);
        using var response = await client.PostAsync("/api/transcribe", AudioForm(Audio()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The transcript really did come back to the caller — so its absence from the log is the
        // logging rule working, not the request having quietly failed.
        var body = await response.Content.ReadFromJsonAsync<TranscribeResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(sentinel, body!.Text);

        var captured = logger.AllCapturedText();
        Assert.DoesNotContain(sentinel, captured, StringComparison.Ordinal);
        Assert.Contains("bytes", captured, StringComparison.Ordinal);
        Assert.Contains("durationMs", captured, StringComparison.Ordinal);
    }

    // --- 7. Nothing intelligible heard ------------------------------------------------------
    /// <summary>
    /// A blank transcript is a normal outcome, not an error: it comes back as 200 with empty text so
    /// the client can show "Không nghe rõ, thử lại." and — crucially — leave whatever the RM had
    /// already typed in the input box untouched.
    /// </summary>
    [Fact]
    public async Task Transcribe_BlankTranscript_Returns200WithEmptyText()
    {
        var (factory, transcriber, _) = CreateFactory();
        await using var factoryDisposable = factory;
        transcriber.Result = "   ";

        using var client = await ChatTestHarness.CreateAuthenticatedClientAsync(factory, TestContext.Current.CancellationToken);
        using var response = await client.PostAsync("/api/transcribe", AudioForm(Audio()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TranscribeResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, body!.Text);
    }
}
