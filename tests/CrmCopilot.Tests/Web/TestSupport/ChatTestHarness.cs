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

    public HttpClient CreateWebClient() => WebFactory.CreateClient();

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

        return new ChatTestHarness
        {
            WebFactory = webFactory,
            McpFactory = mcpFactory,
            McpClient = mcpClient,
            ChatClient = chatClient,
            CrmGateway = crmGateway,
            KnowledgeRetriever = knowledgeRetriever,
            EmailDraftGenerator = emailDraftGenerator,
        };
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
