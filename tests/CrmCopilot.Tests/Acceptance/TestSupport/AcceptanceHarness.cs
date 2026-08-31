using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer;
using CrmCopilot.McpServer.Email;
using CrmCopilot.Tests.Email.TestSupport;
using CrmCopilot.Tests.TestSupport;
using CrmCopilot.Tests.Web.TestSupport;
using CrmCopilot.Web;
using CrmCopilot.Web.Chat;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;

namespace CrmCopilot.Tests.Acceptance.TestSupport;

/// <summary>
/// The stack the deterministic (D) scenarios run against: a real in-memory McpServer backed by
/// <see cref="DatasetCrmGateway"/> (real dataset + real P0-02 search), a real
/// <see cref="McpClient"/> speaking the genuine MCP protocol to it, and — for the scenarios that go
/// through the AI Host — a real Web host whose only fakes are IGeminiChatClient and
/// IMcpClientProvider.
///
/// Same composition idea as <see cref="ChatTestHarness"/>, but parameterized: the acceptance
/// scenarios need a dataset-backed gateway (not the assign-a-result stub), a routing knowledge
/// retriever for generate_email's two searches, and the ability to build an MCP-only stack for the
/// scenarios evaluated at the tool boundary. ChatTestHarness is left untouched — it is P0-05/P0-08
/// test infrastructure and rewriting it would widen this checkpoint's diff for no benefit.
/// </summary>
internal sealed class AcceptanceHarness : IAsyncDisposable
{
    /// <summary>Non-null only for harnesses created with a Web host.</summary>
    private WebApplicationFactory<WebEntryPoint>? WebFactory { get; init; }

    public required WebApplicationFactory<McpServerEntryPoint> McpFactory { get; init; }
    public required McpClient McpClient { get; init; }
    public required DatasetCrmGateway CrmGateway { get; init; }
    public required RoutingKnowledgeRetriever KnowledgeRetriever { get; init; }
    public required FakeEmailDraftGenerator EmailDraftGenerator { get; init; }

    /// <summary>Non-null only for harnesses created with <see cref="CreateWithWebAsync"/>.</summary>
    public FakeGeminiChatClient? ChatClient { get; private init; }

    /// <summary>P0-12: minted once at construction, non-null whenever <see cref="WebFactory"/> is.
    /// Same reasoning as ChatTestHarness — sign in once so CreateWebClient stays synchronous.</summary>
    private string? AuthCookie { get; init; }

    /// <summary>MCP-only stack: for scenarios evaluated directly at the tool boundary (T02/T03/T07).</summary>
    public static Task<AcceptanceHarness> CreateMcpOnlyAsync(CancellationToken cancellationToken) =>
        CreateAsync(cancellationToken, withWeb: false, mcpClientProviderOverride: null);

    /// <summary>Full stack including the Web/AI Host: for scenarios driven through POST /api/chat.</summary>
    public static Task<AcceptanceHarness> CreateWithWebAsync(CancellationToken cancellationToken) =>
        CreateAsync(cancellationToken, withWeb: true, mcpClientProviderOverride: null);

    /// <summary>
    /// Full stack whose Host cannot reach MCP at all — T08's controlled-upstream-failure case.
    /// </summary>
    public static Task<AcceptanceHarness> CreateWithUnreachableMcpAsync(
        Exception connectFailure, CancellationToken cancellationToken) =>
        CreateAsync(cancellationToken, withWeb: true, mcpClientProviderOverride: new FailingMcpClientProvider(connectFailure));

    private static async Task<AcceptanceHarness> CreateAsync(
        CancellationToken cancellationToken, bool withWeb, IMcpClientProvider? mcpClientProviderOverride)
    {
        var crmGateway = new DatasetCrmGateway();
        var knowledgeRetriever = new RoutingKnowledgeRetriever();
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
            }));

        var mcpClient = await ChatTestHarness.ConnectAsync(mcpFactory, cancellationToken).ConfigureAwait(false);

        FakeGeminiChatClient? chatClient = null;
        WebApplicationFactory<WebEntryPoint>? webFactory = null;
        string? authCookie = null;

        if (withWeb)
        {
            chatClient = new FakeGeminiChatClient();
            var provider = mcpClientProviderOverride ?? new PreconnectedMcpClientProvider(mcpClient);
            var capturedChatClient = chatClient;

            webFactory = WebTestHost.CreateWithDefaults()
                .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IGeminiChatClient>();
                    services.AddSingleton<IGeminiChatClient>(capturedChatClient);
                    services.RemoveAll<IMcpClientProvider>();
                    services.AddSingleton(provider);
                }));

            authCookie = await ChatTestHarness.LoginAsync(webFactory, cancellationToken).ConfigureAwait(false);
        }

        return new AcceptanceHarness
        {
            McpFactory = mcpFactory,
            McpClient = mcpClient,
            CrmGateway = crmGateway,
            KnowledgeRetriever = knowledgeRetriever,
            EmailDraftGenerator = emailDraftGenerator,
            ChatClient = chatClient,
            WebFactory = webFactory,
            AuthCookie = authCookie,
        };
    }

    /// <summary>P0-12: every /api/chat scenario runs as the signed-in demo RM.</summary>
    public HttpClient CreateWebClient()
    {
        var factory = WebFactory ?? throw new InvalidOperationException(
            "This harness was created without a Web host — use CreateWithWebAsync for /api/chat scenarios.");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", AuthCookie!);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await McpClient.DisposeAsync().ConfigureAwait(false);
        if (WebFactory is not null)
        {
            await WebFactory.DisposeAsync().ConfigureAwait(false);
        }

        await McpFactory.DisposeAsync().ConfigureAwait(false);
    }
}
