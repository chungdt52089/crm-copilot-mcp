using System.Text.Json;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer;
using CrmCopilot.Tests.Crm.TestSupport;
using CrmCopilot.Tests.Knowledge.TestSupport;
using CrmCopilot.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CrmCopilot.Tests.Mcp;

/// <summary>
/// Real MCP protocol-level coverage (AC-M01/M02): boots the actual McpServer host in-memory via
/// WebApplicationFactory, connects with a real McpClient over HttpClientTransport pointed at the
/// factory's in-memory HttpClient (no real socket/port), and drives tools/list + tools/call
/// through the genuine MCP JSON-RPC layer. ICrmGateway/IKnowledgeRetriever are DI-overridden with
/// fakes (last-registration-wins) — the HTTP-mapping layer under those interfaces is already
/// covered by MockCrmGatewayTests/KnowledgeRetrieverTests; this suite is scoped to the MCP wire
/// protocol itself, not re-testing that layer.
/// </summary>
public class McpToolProtocolTests
{
    private static readonly CustomerDto Cus0001 = new(
        "CUS-0001", "Nguyễn Minh Anh", "test@example.com", "0900000000", "ACC-0001",
        "Priority", "Hà Nội", "vi", "RM-0001", "Active", true, DateTime.UtcNow);

    private static (WebApplicationFactory<McpServerEntryPoint> Factory, FakeCrmGateway Crm, FakeKnowledgeRetriever Knowledge) CreateFactory()
    {
        var crmGateway = new FakeCrmGateway();
        var knowledgeRetriever = new FakeKnowledgeRetriever();

        var factory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl)
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICrmGateway>();
                services.AddSingleton<ICrmGateway>(crmGateway);
                services.RemoveAll<IKnowledgeRetriever>();
                services.AddSingleton<IKnowledgeRetriever>(knowledgeRetriever);
            }));

        return (factory, crmGateway, knowledgeRetriever);
    }

    private static async Task<McpClient> ConnectAsync(WebApplicationFactory<McpServerEntryPoint> factory, CancellationToken cancellationToken)
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

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static JsonElement ParseTextResult(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        return JsonDocument.Parse(text).RootElement;
    }

    [Fact]
    public async Task ToolsList_ReturnsExactlyThreeExpectedToolsWithNonEmptySchemas()
    {
        var (factory, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var names = tools.Select(tool => tool.Name).ToList();
        Assert.Equal(
            new[] { "get_customer", "get_interactions", "search_product_knowledge" },
            names.OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.JsonSchema.ValueKind == JsonValueKind.Undefined));
    }

    [Fact]
    public async Task GetCustomer_CanonicalSuccess_RoundTripsThroughRealMcpProtocol()
    {
        var (factory, crmGateway, _) = CreateFactory();
        crmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "get_customer",
            new Dictionary<string, object?> { ["customerId"] = "CUS-0001" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal("CUS-0001", root.GetProperty("data").GetProperty("customer").GetProperty("id").GetString());
        Assert.Equal("crm:customer:CUS-0001", root.GetProperty("sourceIds")[0].GetString());
        Assert.True(root.GetProperty("traceId").GetString()!.Length > 0);
    }

    [Fact]
    public async Task GetInteractions_CanonicalSuccess_RoundTripsThroughRealMcpProtocol()
    {
        var (factory, crmGateway, _) = CreateFactory();
        crmGateway.InteractionsResult = [new InteractionDto("INT-0001", "CUS-0001", "Call", DateTime.UtcNow, "summary", "outcome", null, true)];
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "get_interactions",
            new Dictionary<string, object?> { ["customerId"] = "CUS-0001", ["limit"] = 5 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal("crm:interaction:INT-0001", root.GetProperty("sourceIds")[0].GetString());
    }

    [Fact]
    public async Task SearchProductKnowledge_CanonicalSuccess_RoundTripsThroughRealMcpProtocol()
    {
        var (factory, _, knowledgeRetriever) = CreateFactory();
        var metadata = new KnowledgeSourceMetadata(
            "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
            "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fingerprint");
        knowledgeRetriever.SearchResult = KnowledgeSearchResult.Found(
            [new KnowledgeMatch("kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "nội dung", metadata, Distance: 0.47)]);
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "search_product_knowledge",
            new Dictionary<string, object?> { ["query"] = "Khách hàng quan tâm gửi tiết kiệm an toàn kỳ hạn 6 tháng", ["topK"] = 3 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        var match = root.GetProperty("data").GetProperty("matches")[0];
        Assert.Equal("kb:product:PRD-SAV-006M", root.GetProperty("sourceIds")[0].GetString());
        Assert.True(match.GetProperty("distance").GetDouble() > 0);
        Assert.False(match.TryGetProperty("title", out _));
        Assert.False(match.TryGetProperty("score", out _));
    }

    [Fact]
    public async Task SearchProductKnowledge_TopKOutOfRange_ReturnsStructuredInvalidArgument()
    {
        var (factory, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "search_product_knowledge",
            new Dictionary<string, object?> { ["query"] = "query", ["topK"] = 6 },
            cancellationToken: TestContext.Current.CancellationToken);

        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCustomer_NotFound_ReturnsStructuredNotFound()
    {
        var (factory, crmGateway, _) = CreateFactory();
        crmGateway.FindCustomerResult = CustomerLookupResult.NotFound;
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "get_customer",
            new Dictionary<string, object?> { ["customerId"] = "CUS-9999" },
            cancellationToken: TestContext.Current.CancellationToken);

        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCustomer_Ambiguous_ReturnsStructuredAmbiguous()
    {
        var (factory, crmGateway, _) = CreateFactory();
        crmGateway.FindCustomerResult = CustomerLookupResult.Ambiguous(
            [new CustomerCandidateDto("CUS-0002", "Trần Thị Hương", "Priority", "Hà Nội"),
             new CustomerCandidateDto("CUS-0003", "Trần Thị Hương", "Standard", "Đà Nẵng")]);
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "get_customer",
            new Dictionary<string, object?> { ["query"] = "Trần Thị Hương" },
            cancellationToken: TestContext.Current.CancellationToken);

        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Ambiguous, root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("data").GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task GetCustomer_UpstreamUnavailable_ReturnsStructuredErrorWithoutLeakingExceptionDetails()
    {
        var (factory, crmGateway, _) = CreateFactory();
        crmGateway.ThrowOnFindCustomer = new CrmUpstreamException("internal-detail-should-not-leak", retryable: true, traceId: null);
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "get_customer",
            new Dictionary<string, object?> { ["customerId"] = "CUS-0001" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.UpstreamUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("internal-detail-should-not-leak", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CrmUpstreamException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchProductKnowledge_RagUnavailable_ReturnsStructuredErrorWithoutLeakingExceptionDetails()
    {
        var (factory, _, knowledgeRetriever) = CreateFactory();
        knowledgeRetriever.ThrowOnSearch = new KnowledgeEmbeddingException("internal-detail-should-not-leak", retryable: true);
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "search_product_knowledge",
            new Dictionary<string, object?> { ["query"] = "query" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.RagUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("internal-detail-should-not-leak", text, StringComparison.Ordinal);
        Assert.DoesNotContain("KnowledgeEmbeddingException", text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
    }
}
