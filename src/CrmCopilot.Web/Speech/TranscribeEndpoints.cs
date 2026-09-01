using System.Diagnostics;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Web.Chat;

namespace CrmCopilot.Web.Speech;

/// <summary>
/// P0-15 (WP4): POST /api/transcribe. Mirrors ChatEndpoints' shape — flat MapPost,
/// .RequireAuthorization(), handler as a private static method, error bodies reusing ChatTurnError.
///
/// This endpoint deliberately does NOT run InputGuard. A transcript is not a chat message yet: the
/// RM has to read it, fix it, and press Gửi, and only that path reaches
/// ChatOrchestrator.HandleAsync's InputGuard.Validate call. Guarding here would reject a spoken
/// customer name before the RM could replace it with a code — destroying the very control the
/// confirmation step exists to provide (docs/15 §WP4: "Bước RM xác nhận là một control PII").
/// </summary>
internal static class TranscribeEndpoints
{
    /// <summary>Spike A measured ~14 KB/s for audio/webm;codecs=opus. 1 MB is the Product-Owner-set
    /// cap; the browser separately stops recording at 15 s. The two limits guard different things and
    /// are deliberately not collapsed into one.</summary>
    internal const long MaxAudioBytes = 1_048_576;

    internal const string FormFieldName = "audio";

    private const string NoAudioMessage = "Không nhận được dữ liệu âm thanh.";
    private const string UnsupportedTypeMessage = "Định dạng âm thanh không được hỗ trợ.";
    private const string TooLargeMessage = "Bản ghi âm vượt quá 1 MB.";
    private const string TranscribeFailedMessage = "Không thể nhận dạng giọng nói.";

    public static IEndpointRouteBuilder MapTranscribeEndpoints(this IEndpointRouteBuilder app)
    {
        // DisableAntiforgery is required, not cosmetic: minimal-API IFormFile binding attaches
        // antiforgery metadata, and Program.cs has no UseAntiforgery() middleware, so without this
        // every request throws before the handler runs. The endpoint is cookie-authenticated,
        // same-origin, and mutates nothing (see the checkpoint's known-limitations note).
        app.MapPost("/api/transcribe", HandleAsync)
            .RequireAuthorization()
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> HandleAsync(
        IFormFile? audio,
        ITranscriber transcriber,
        ILogger<GeminiTranscriber> logger,
        CancellationToken cancellationToken)
    {
        if (audio is null || audio.Length == 0)
        {
            return Invalid(NoAudioMessage);
        }

        // WP4: only audio/*. ContentType arrives as "audio/webm;codecs=opus"; Part.FromBytes wants a
        // bare IANA type and will not infer one, so the parameters are dropped here.
        var mimeType = (audio.ContentType ?? string.Empty).Split(';')[0].Trim();
        if (!mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(UnsupportedTypeMessage);
        }

        if (audio.Length > MaxAudioBytes)
        {
            return Invalid(TooLargeMessage);
        }

        var stopwatch = Stopwatch.StartNew();
        string text;
        try
        {
            // Read into memory and let it go out of scope — WP4: audio is never written to disk.
            // AddSpeechTranscription raises FormOptions.MemoryBufferThreshold above MaxAudioBytes so
            // the multipart reader cannot spill it to a temp file either.
            using var buffer = new MemoryStream(capacity: (int)audio.Length);
            await audio.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            var raw = await transcriber
                .TranscribeAsync(buffer.ToArray(), mimeType, cancellationToken)
                .ConfigureAwait(false);

            text = TranscriptNormalizer.Normalize(raw).Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ChatModelException ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Transcribe failed durationMs={DurationMs} bytes={Bytes}", stopwatch.ElapsedMilliseconds, audio.Length);
            return TypedResults.Json(
                new ChatTurnError(ChatTurnErrorCode.ModelError, TranscribeFailedMessage, ex.Retryable),
                statusCode: StatusCodes.Status502BadGateway);
        }

        stopwatch.Stop();

        // Sizes and timings only. The transcript itself is never logged (WP4 constraint) — textLength
        // is what makes a blank result diagnosable without recording what was said.
        logger.LogInformation(
            "Transcribe status=success bytes={Bytes} durationMs={DurationMs} textLength={TextLength}",
            audio.Length, stopwatch.ElapsedMilliseconds, text.Length);

        return TypedResults.Ok(new TranscribeResponse(text));
    }

    private static IResult Invalid(string message) =>
        TypedResults.Json(
            new ChatTurnError(ChatTurnErrorCode.InvalidArgument, message, Retryable: false),
            statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>
/// P0-15 transcribe result. Text only — the endpoint returns what the RM should see in the input box,
/// never the audio, never a model diagnostic. An empty string is a valid outcome ("nghe không rõ") and
/// tells the client to leave whatever the RM already typed alone.
/// </summary>
internal sealed record TranscribeResponse(string Text);
