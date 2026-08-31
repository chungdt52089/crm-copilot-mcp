using System.Net.Http.Json;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer;
using CrmCopilot.McpServer.Email;
using CrmCopilot.Tests.Crm.TestSupport;
using CrmCopilot.Tests.Email.TestSupport;
using CrmCopilot.Tests.Knowledge.TestSupport;
using CrmCopilot.Tests.TestSupport;
using CrmCopilot.Web;
using CrmCopilot.Web.Chat;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;

namespace CrmCopilot.Tests.Web.TestSupport;

/// <summary>
/// Composes a full P0-05 test double stack: a real in-memory McpServer (backed by
/// FakeCrmGateway/FakeKnowledgeRetriever — same fakes/pattern as P0-04's McpToolProtocolTests) and
/// a real McpClient connected to it, wired into a Web host whose only fakes are
/// IGeminiChatClient/IMcpClientProvider. Every ListToolsAsync/CallToolAsync in a test using this
/// harness travels the real MCP protocol end to end — only the two genuinely-external/paid
/// dependencies (Gemini, and the McpClient's own creation) are stood in for.
/// </summary>
internal sealed class ChatTestHarness : IAsyncDisposable
{
    public required WebApplicationFactory<WebEntryPoint> WebFactory { get; init; }
    public required WebApplicationFactory<McpServerEntryPoint> McpFactory { get; init; }
    public required McpClient McpClient { get; init; }
    public required FakeGeminiChatClient ChatClient { get; init; }
    public required FakeCrmGateway CrmGateway { get; init; }
    public required FakeKnowledgeRetriever KnowledgeRetriever { get; init; }
    public required FakeEmailDraftGenerator EmailDraftGenerator { get; init; }

    /// <summary>P0-12: the auth cookie minted once in <see cref="CreateAsync"/>, replayed on every
    /// client this harness hands out.</summary>
    public required string AuthCookie { get; init; }

    /// <summary>
    /// P0-12: /api/chat now requires an authenticated cookie. The sign-in happens once, in the
    /// async factory, and the resulting cookie is replayed here — so this stays synchronous and
    /// none of its ~58 call sites had to change shape.
    /// </summary>
    public HttpClient CreateWebClient()
    {
        var client = WebFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", AuthCookie);
        return client;
    }

    /// <summary>An unauthenticated client, for tests that assert the 401 path itself.</summary>
    public HttpClient CreateAnonymousWebClient() => WebFactory.CreateClient();

    public static async Task<ChatTestHarness> CreateAsync(CancellationToken cancellationToken, bool includeExtraTool = false)
    {
        var crmGateway = new FakeCrmGateway();
        var knowledgeRetriever = new FakeKnowledgeRetriever();
        var emailDraftGenerator = new FakeEmailDraftGenerator();

        var mcpFactory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl)
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICrmGateway>();
                services.AddSingleton<ICrmGateway>(crmGateway);
                services.RemoveAll<IKnowledgeRetriever>();
                services.AddSingleton<IKnowledgeRetriever>(knowledgeRetriever);
                services.RemoveAll<IEmailDraftGenerator>();
                services.AddSingleton<IEmailDraftGenerator>(emailDraftGenerator);
                if (includeExtraTool)
                {
                    services.AddMcpServer().WithTools<ExtraTestOnlyTool>();
                }
            }));

        var mcpClient = await ConnectAsync(mcpFactory, cancellationToken).ConfigureAwait(false);
        var chatClient = new FakeGeminiChatClient();
        var mcpClientProvider = new PreconnectedMcpClientProvider(mcpClient);

        var webFactory = WebTestHost.CreateWithDefaults()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGeminiChatClient>();
                services.AddSingleton<IGeminiChatClient>(chatClient);
                services.RemoveAll<IMcpClientProvider>();
                services.AddSingleton<IMcpClientProvider>(mcpClientProvider);
            }));

        var authCookie = await LoginAsync(webFactory, cancellationToken).ConfigureAwait(false);

        return new ChatTestHarness
        {
            WebFactory = webFactory,
            McpFactory = mcpFactory,
            McpClient = mcpClient,
            ChatClient = chatClient,
            CrmGateway = crmGateway,
            KnowledgeRetriever = knowledgeRetriever,
            EmailDraftGenerator = emailDraftGenerator,
            AuthCookie = authCookie,
        };
    }

    /// <summary>
    /// Signs in against the real POST /api/auth/login using the synthetic demo credentials from
    /// data/auth/users.json, and returns the cookie in request-header form (<c>name=value</c>, the
    /// first segment of Set-Cookie — the attributes after it are response-only).
    ///
    /// Shared with <c>AcceptanceHarness</c>, which composes the same Web host. Deliberately the
    /// real endpoint rather than a stub auth scheme: the sign-in path is then covered by every
    /// test that talks to /api/chat.
    /// </summary>
    public static async Task<string> LoginAsync(
        WebApplicationFactory<WebEntryPoint> webFactory, CancellationToken cancellationToken,
        string userId = DefaultUserId, string password = DefaultPassword)
    {
        using var client = webFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { userId, password }, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Test harness sign-in for '{userId}' failed with HTTP {(int)response.StatusCode}.");
        }

        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault()
            : null;

        return setCookie is { Length: > 0 }
            ? setCookie.Split(';')[0]
            : throw new InvalidOperationException("Test harness sign-in returned no Set-Cookie header.");
    }

    public const string DefaultUserId = "rm01";
    public const string DefaultPassword = "Demo@123";

    /// <summary>
    /// For the few tests that compose their own Web host inline instead of using this harness
    /// (the controlled MCP-transport-failure cases in ChatEndpointTests) — signs in and returns a
    /// client already carrying the cookie.
    /// </summary>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<WebEntryPoint> webFactory, CancellationToken cancellationToken)
    {
        var cookie = await LoginAsync(webFactory, cancellationToken).ConfigureAwait(false);
        var client = webFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    /// <summary>Same connection recipe as McpToolProtocolTests.ConnectAsync (P0-04) — kept
    /// separate here since Tests has no shared helper for it yet and this harness needs it too.</summary>
    public static async Task<McpClient> ConnectAsync(
        WebApplicationFactory<McpServerEntryPoint> factory, CancellationToken cancellationToken)
    {
        var httpClient = factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await McpClient.DisposeAsync().ConfigureAwait(false);
        await WebFactory.DisposeAsync().ConfigureAwait(false);
        await McpFactory.DisposeAsync().ConfigureAwait(false);
    }
}
