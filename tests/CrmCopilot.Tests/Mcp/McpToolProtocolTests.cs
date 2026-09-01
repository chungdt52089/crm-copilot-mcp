using System.Text.Json;
using CrmCopilot.Contracts.Auth;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer;
using CrmCopilot.McpServer.Email;
using CrmCopilot.Tests.Crm.TestSupport;
using CrmCopilot.Tests.Email.TestSupport;
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

    private static (WebApplicationFactory<McpServerEntryPoint> Factory, FakeCrmGateway Crm, FakeKnowledgeRetriever Knowledge, FakeEmailDraftGenerator Email) CreateFactory()
    {
        var crmGateway = new FakeCrmGateway();
        var knowledgeRetriever = new FakeKnowledgeRetriever();
        var emailDraftGenerator = new FakeEmailDraftGenerator();

        var factory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl)
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICrmGateway>();
                services.AddSingleton<ICrmGateway>(crmGateway);
                services.RemoveAll<IKnowledgeRetriever>();
                services.AddSingleton<IKnowledgeRetriever>(knowledgeRetriever);
                services.RemoveAll<IEmailDraftGenerator>();
                services.AddSingleton<IEmailDraftGenerator>(emailDraftGenerator);
            }));

        return (factory, crmGateway, knowledgeRetriever, emailDraftGenerator);
    }

    /// <summary>P0-14: <paramref name="role"/> defaults to Admin so every pre-existing test keeps
    /// exercising the same tool set it did before; the delete-authorization tests pass RM.</summary>
    private static async Task<McpClient> ConnectAsync(
        WebApplicationFactory<McpServerEntryPoint> factory, CancellationToken cancellationToken, string role = Roles.Admin)
    {
        var httpClient = factory.CreateClient();
        var transport = new HttpClientTransport(
            McpTestTransport.Options(httpClient, McpTestTokens.AuthorizationHeader(role)),
            httpClient,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static JsonElement ParseTextResult(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        return JsonDocument.Parse(text).RootElement;
    }

    /// <summary>
    /// The P0-14 target set (docs/07 §2: exactly eight tools). Asserted as an exact, ordered set
    /// rather than a count so both a missing registration and an unintended extra tool fail loudly —
    /// .WithTools&lt;T&gt;() in Program.cs is the only thing that puts a tool here.
    ///
    /// Note what is NOT asserted anywhere: that this list varies by role. It must not. tools/list is
    /// deliberately unfiltered, so every role — RM, Auditor, Admin — discovers all eight (PD-022).
    /// </summary>
    private static readonly string[] ExpectedToolNames =
    [
        "delete_customer",
        "generate_call_script",
        "generate_email",
        "get_campaigns",
        "get_customer",
        "get_interactions",
        "get_opportunities",
        "search_product_knowledge",
    ];

    [Fact]
    public async Task ToolsList_ReturnsExactlyEightExpectedToolsWithNonEmptySchemas()
    {
        var (factory, _, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var names = tools.Select(tool => tool.Name).ToList();
        Assert.Equal(ExpectedToolNames, names.OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.False(tool.JsonSchema.ValueKind == JsonValueKind.Undefined));
    }

    /// <summary>
    /// docs/07 §8, as overridden by PD-021: the seven READ tools stay read-only and non-destructive,
    /// and no send tool exists. Asserted across the whole set rather than for one tool, so a future
    /// tool cannot be added without this annotation contract being considered.
    ///
    /// delete_customer is excluded here and pinned by its own test below — the exclusion is the
    /// point, so it is written as an explicit filter rather than a loosened assertion.
    /// </summary>
    [Fact]
    public async Task ToolsList_EveryReadToolAnnotatedReadOnlyTrueDestructiveFalse()
    {
        var (factory, _, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(tools.Where(tool => tool.Name != "delete_customer"), tool =>
        {
            var annotations = tool.ProtocolTool.Annotations;
            Assert.Equal(true, annotations?.ReadOnlyHint);
            Assert.NotEqual(true, annotations?.DestructiveHint);
        });

        Assert.DoesNotContain(tools, tool => tool.Name == "send_email");
    }

    /// <summary>
    /// P0-14 (PD-021). delete_customer is the project's only write tool, and the annotation contract
    /// says so on the wire. The final assertion is the load-bearing one: it is the ONLY destructive
    /// tool, so a second one cannot be introduced without this failing.
    /// </summary>
    [Fact]
    public async Task ToolsList_DeleteCustomerIsTheOnlyWriteDestructiveTool()
    {
        var (factory, _, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var deleteTool = Assert.Single(tools, tool => tool.Name == "delete_customer");
        Assert.NotEqual(true, deleteTool.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Equal(true, deleteTool.ProtocolTool.Annotations?.DestructiveHint);

        Assert.Single(tools, tool => tool.ProtocolTool.Annotations?.DestructiveHint == true);
    }

    // --- P0-14 delete_customer (PD-021/PD-023/PD-024) ---

    [Fact]
    public async Task DeleteCustomer_AsAdmin_ReturnsSuccessWithCustomerSourceId()
    {
        var (factory, crmGateway, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "delete_customer",
            new Dictionary<string, object?> { ["customerId"] = "CUS-0001" },
            cancellationToken: TestContext.Current.CancellationToken);

        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(
            "crm:customer:CUS-0001",
            Assert.Single(root.GetProperty("sourceIds").EnumerateArray()).GetString());
        Assert.Equal("CUS-0001", root.GetProperty("data").GetProperty("customerId").GetString());
        Assert.Equal("CUS-0001", crmGateway.LastDeletedCustomerId);
    }

    /// <summary>
    /// AC-5, and the reason ToolAuthorizationFilter is a request filter rather than a check copied
    /// into each tool body: the refusal must happen BEFORE the gateway is touched, not after a
    /// delete has already been performed. LastDeletedCustomerId staying null is what proves it —
    /// asserting only on the FORBIDDEN envelope would pass even if the customer had been deleted
    /// first and the error returned afterwards.
    /// </summary>
    [Fact]
    public async Task DeleteCustomer_AsRm_ReturnsForbiddenWithoutTouchingGateway()
    {
        var (factory, crmGateway, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken, Roles.RM);

        var result = await client.CallToolAsync(
            "delete_customer",
            new Dictionary<string, object?> { ["customerId"] = "CUS-0001" },
            cancellationToken: TestContext.Current.CancellationToken);

        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.Forbidden, root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Empty(root.GetProperty("sourceIds").EnumerateArray());

        Assert.Null(crmGateway.LastDeletedCustomerId);
    }

    /// <summary>
    /// A malformed id is refused as malformed, not forwarded to come back NOT_FOUND — which would
    /// assert the id was well-formed and merely absent. The gateway is never reached either.
    /// </summary>
    [Fact]
    public async Task DeleteCustomer_MalformedCustomerId_ReturnsInvalidArgumentWithoutTouchingGateway()
    {
        var (factory, crmGateway, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "delete_customer",
            new Dictionary<string, object?> { ["customerId"] = "CS-0003" },
            cancellationToken: TestContext.Current.CancellationToken);

        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(CustomerIdFormat.InvalidMessage, root.GetProperty("error").GetProperty("message").GetString());

        Assert.Null(crmGateway.LastDeletedCustomerId);
    }

    [Fact]
    public async Task GenerateEmail_CanonicalSuccess_RoundTripsThroughRealMcpProtocol()
    {
        var (factory, crmGateway, knowledgeRetriever, emailDraftGenerator) = CreateFactory();
        crmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        crmGateway.InteractionsResult = [new InteractionDto("INT-0001", "CUS-0001", "Call", DateTime.UtcNow, "summary", "outcome", null, true)];
        var metadata = new KnowledgeSourceMetadata(
            "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
            "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fingerprint");
        knowledgeRetriever.SearchResult = KnowledgeSearchResult.Found(
            [new KnowledgeMatch("kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "nội dung", metadata, Distance: 0.4)]);
        emailDraftGenerator.Results.Enqueue(new RawEmailDraftModel(
            RawEmailDraftModel.StatusOk, "Thông tin tham khảo",
            "Kính gửi {{CUSTOMER_NAME}},\n\nNội dung tham khảo.\n\nTrân trọng,",
            "PRD-SAV-006M", ["kb:product:PRD-SAV-006M"], true, []));
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "generate_email",
            new Dictionary<string, object?> { ["customerId"] = "CUS-0001", ["objective"] = "Follow-up nhu cầu gửi tiết kiệm" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(true, result.IsError);
        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("data").GetProperty("draft").GetProperty("requiresHumanApproval").GetBoolean());
    }

    [Fact]
    public async Task GenerateEmail_CustomerNotFound_ReturnsStructuredNotFound()
    {
        var (factory, crmGateway, _, _) = CreateFactory();
        crmGateway.FindCustomerResult = CustomerLookupResult.NotFound;
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "generate_email",
            new Dictionary<string, object?> { ["customerId"] = "CUS-9999", ["objective"] = "objective" },
            cancellationToken: TestContext.Current.CancellationToken);

        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GenerateEmail_InvalidTone_ReturnsStructuredInvalidArgument()
    {
        var (factory, _, _, _) = CreateFactory();
        await using var factoryDisposable = factory;
        await using var client = await ConnectAsync(factory, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "generate_email",
            new Dictionary<string, object?> { ["customerId"] = "CUS-0001", ["objective"] = "objective", ["tone"] = "friendly" },
            cancellationToken: TestContext.Current.CancellationToken);

        var root = ParseTextResult(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCustomer_CanonicalSuccess_RoundTripsThroughRealMcpProtocol()
    {
        var (factory, crmGateway, _, _) = CreateFactory();
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
        var (factory, crmGateway, _, _) = CreateFactory();
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
        var (factory, _, knowledgeRetriever, _) = CreateFactory();
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
        var (factory, _, _, _) = CreateFactory();
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
        var (factory, crmGateway, _, _) = CreateFactory();
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
        var (factory, crmGateway, _, _) = CreateFactory();
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
        var (factory, crmGateway, _, _) = CreateFactory();
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
        var (factory, _, knowledgeRetriever, _) = CreateFactory();
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
