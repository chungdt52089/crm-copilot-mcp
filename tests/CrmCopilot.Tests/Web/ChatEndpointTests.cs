using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.Tests.TestSupport;
using CrmCopilot.Tests.Web.TestSupport;
using CrmCopilot.Web.Chat;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// P0-05 end-to-end coverage (plan §7): a real Web host driving a real MCP client against a real
/// in-memory McpServer (fakes only at ICrmGateway/IKnowledgeRetriever, exactly the P0-04
/// McpToolProtocolTests pattern) — only IGeminiChatClient is faked (the genuinely
/// external/paid/quota'd dependency). Because FakeCrmGateway/FakeKnowledgeRetriever exist only
/// inside the in-memory McpServer's own DI container, any assertion below that reads their
/// Last*/captured state is only satisfiable via the real MCP client→server→gateway chain — there
/// is no other path by which Web could reach them (structural no-bypass proof, scenario 21).
/// </summary>
public class ChatEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly CustomerDto Cus0001 = new(
        "CUS-0001", "Nguyễn Minh Anh", "minh.anh@example.test", "0900000001", "000000000001",
        "Priority", "Hà Nội", "vi", "RM-001", "Active", true, DateTime.UtcNow);

    private static readonly InteractionDto Int0001 = new(
        "INT-0001", "CUS-0001", "Call", DateTime.UtcNow,
        "Khách hàng quan tâm tiền gửi kỳ hạn 6 tháng.", "FollowUpRequired", null, true);

    private static KnowledgeMatch SavingsMatch() => new(
        "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product,
        "Sản phẩm tiền gửi kỳ hạn 6 tháng dành cho khách hàng ưu tiên an toàn.",
        new KnowledgeSourceMetadata(
            "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
            "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fingerprint-1"),
        Distance: 0.47);

    private static async Task<(HttpResponseMessage Response, ChatResponse Body)> PostChatAsync(HttpClient client, string message)
    {
        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest(message), JsonOptions, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return (response, body!);
    }

    // --- 1. Customer lookup by ID ---
    [Fact]
    public async Task CustomerLookupById_CallsGetCustomer_ReturnsCustomerData()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Đã tìm thấy khách hàng CUS-0001."));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal("CUS-0001", body.Data?.Customer?.Id);
        Assert.Contains("crm:customer:CUS-0001", body.SourceIds);
        Assert.Single(body.ToolTrace);
        Assert.Equal("get_customer", body.ToolTrace[0].ToolName);
        Assert.Equal("success", body.ToolTrace[0].Status);
    }

    // --- 2. Interaction lookup ---
    [Fact]
    public async Task InteractionLookup_CallsGetInteractions_ReturnsInteractionData()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_interactions", new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["limit"] = 5 }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Đây là các tương tác gần đây."));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Xem các tương tác gần đây của CUS-0001");

        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.NotNull(body.Data?.Interactions);
        Assert.Contains("crm:interaction:INT-0001", body.SourceIds);
        Assert.Equal("CUS-0001", harness.CrmGateway.LastInteractionsCustomerId);
        Assert.Equal(5, harness.CrmGateway.LastInteractionsLimit);
    }

    // --- 3. Two-tool turn: CRM lookup + knowledge search, reply grounded only in knowledge ---
    [Fact]
    public async Task TwoToolTurn_ReplyGroundedOnlyInKnowledgeContent_CustomerDataStillInResponse()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "gửi tiết kiệm an toàn kỳ hạn 6 tháng" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Gợi ý sản phẩm PRD-SAV-006M phù hợp với nhu cầu tiết kiệm an toàn."));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Khách hàng CUS-0001 quan tâm gửi tiết kiệm, gợi ý sản phẩm phù hợp");

        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.NotNull(body.Reply);
        Assert.DoesNotContain(Cus0001.FullName, body.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Email, body.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Phone, body.Reply, StringComparison.Ordinal);
        Assert.Equal("CUS-0001", body.Data?.Customer?.Id);
        Assert.Contains("crm:customer:CUS-0001", body.SourceIds);
        Assert.Contains("kb:product:PRD-SAV-006M", body.SourceIds);

        // D1: the FunctionResponse sent back to Gemini after get_customer must never contain raw PII.
        var secondCallContents = harness.ChatClient.CapturedContents[1];
        var serializedHistory = JsonSerializer.Serialize(secondCallContents.Select(c => c.Parts));
        Assert.DoesNotContain(Cus0001.Email, serializedHistory, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Phone, serializedHistory, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.AccountReference, serializedHistory, StringComparison.Ordinal);
    }

    // --- 4. Unknown customer (not_found) — deterministic, no second Gemini call (D1) ---
    [Fact]
    public async Task UnknownCustomer_ReturnsDeterministicNotFound_NoSecondGeminiCall()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.NotFound;
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-9999" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ChatTurnStatus.NotFound, body.Status);
        Assert.Null(body.Reply);
        Assert.Equal(1, harness.ChatClient.CallCount);
    }

    // --- 6. Extra MCP tool present but not approved — never exposed to Gemini (D5) ---
    [Fact]
    public async Task ExtraMcpTool_NeverExposedToGemini()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken, includeExtraTool: true);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Sản phẩm tiết kiệm 6 tháng có lãi suất minh họa niêm yết công khai."));

        await PostChatAsync(harness.CreateWebClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        var sentTools = harness.ChatClient.CapturedConfigs[0].Tools;
        var declarations = Assert.Single(sentTools!).FunctionDeclarations!;
        Assert.Equal(3, declarations.Count);
        Assert.DoesNotContain(declarations, d => d.Name == "delete_customer");
        Assert.Contains(declarations, d => d.Name == "get_customer");
        Assert.Contains(declarations, d => d.Name == "get_interactions");
        Assert.Contains(declarations, d => d.Name == "search_product_knowledge");
    }

    // --- 7. Hallucinated unknown tool name — rejected before any MCP call ---
    [Fact]
    public async Task HallucinatedUnknownTool_RejectedWithoutAnyMcpCall()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse("send_email", []));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.Equal(ChatTurnErrorCode.UnknownTool, body.Error?.Code);
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Null(harness.CrmGateway.LastLookupQuery);
        Assert.Null(harness.KnowledgeRetriever.LastQuery);
    }

    // --- 8. Duplicate identical tool+args — the exact shape of the live P0-05 acceptance finding:
    // get_customer succeeds once, Gemini's next turn asks for the identical call again. The guard
    // must still block the 2nd MCP call (unchanged), AND the resulting error must not leak the
    // already-fetched customer's raw PII (this was the live finding's actual bug). ---
    [Fact]
    public async Task DuplicateToolCall_RejectedOnSecondIdenticalRequest_ErrorResponseCarriesNoAccumulatedPii()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        // Guard still fires — exactly one real MCP call happened (2 Gemini calls: 1st succeeds,
        // 2nd is rejected as a duplicate before ever reaching MCP again).
        Assert.Equal(ChatTurnErrorCode.DuplicateToolCall, body.Error?.Code);
        Assert.Equal(2, harness.ChatClient.CallCount);

        // Fix for the live finding: Data must be null on a controlled error — never the earlier
        // successful call's raw CustomerDto.
        Assert.Null(body.Data);
        var bodyText = JsonSerializer.Serialize(body);
        Assert.DoesNotContain(Cus0001.FullName, bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Email, bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Phone, bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.AccountReference, bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.City, bodyText, StringComparison.Ordinal);
    }

    // --- New: the exact live-finding fix — a successful get_customer call's minimized
    // FunctionResponse must include a non-PII customerId (not just a bare status ack), and Gemini
    // seeing it must be able to produce a final reply instead of repeating the call. ---
    [Fact]
    public async Task CustomerLookup_MinimizedFunctionResponseIncludesCustomerId_GeminiCanProduceFinalReply()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Đã tìm thấy khách hàng CUS-0001."));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal(2, harness.ChatClient.CallCount);

        // Inspect exactly what the 2nd Gemini call received as the FunctionResponse payload.
        var secondCallContents = harness.ChatClient.CapturedContents[1];
        var functionResponsePart = secondCallContents[^1].Parts!.Single();
        var functionResponseJson = JsonSerializer.Serialize(functionResponsePart.FunctionResponse!.Response);
        Assert.Contains("\"status\":\"success\"", functionResponseJson, StringComparison.Ordinal);
        Assert.Contains("CUS-0001", functionResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.FullName, functionResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Email, functionResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Phone, functionResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.AccountReference, functionResponseJson, StringComparison.Ordinal);
    }

    // --- 9. Multiple function calls in one Gemini turn — rejected outright (D8) ---
    [Fact]
    public async Task MultipleFunctionCallsInOneTurn_RejectedWithoutAnyMcpCall()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.MultiFunctionCallResponse(
            ("get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }),
            ("get_interactions", new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["limit"] = 5 })));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(ChatTurnErrorCode.MultipleFunctionCallsNotSupported, body.Error?.Code);
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Null(harness.CrmGateway.LastLookupQuery);
    }

    // --- 10. Always-request-another-tool — bounded at 3 MCP calls, 4th Gemini call still happens ---
    [Fact]
    public async Task AlwaysRequestsAnotherTool_StopsAtThreeMcpCalls_FourthGeminiCallRejectsIt()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);

        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_interactions", new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["limit"] = 5 }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "truy vấn A" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "truy vấn B (khác truy vấn A)" }));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(ChatTurnErrorCode.ToolLoopLimitExceeded, body.Error?.Code);
        Assert.Equal(4, harness.ChatClient.CallCount);
        Assert.Equal(3, body.ToolTrace.Count);
    }

    // --- 11. Exactly 3 tools needed, 4th Gemini turn returns final text — legitimate success ---
    [Fact]
    public async Task ExactlyThreeToolsThenFinalText_SucceedsWithFourGeminiCallsThreeMcpCalls()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);

        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_interactions", new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["limit"] = 5 }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "truy vấn A" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Đã tổng hợp đầy đủ thông tin."));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.NotNull(body.Reply);
        Assert.Equal(4, harness.ChatClient.CallCount);
        Assert.Equal(3, body.ToolTrace.Count);
    }

    // --- 12. Mock CRM upstream unavailable — normalized, no raw exception text leaked ---
    [Fact]
    public async Task CrmUpstreamUnavailable_ReturnsStructuredErrorWithoutLeakingExceptionDetails()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.ThrowOnFindCustomer = new CrmUpstreamException("internal-detail-should-not-leak", retryable: true, traceId: null);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.UpstreamUnavailable, body.Error?.Code);
        var bodyText = JsonSerializer.Serialize(body);
        Assert.DoesNotContain("internal-detail-should-not-leak", bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("CrmUpstreamException", bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", bodyText, StringComparison.Ordinal);
    }

    // --- 13. RAG unavailable — normalized, no raw exception text leaked ---
    [Fact]
    public async Task RagUnavailable_ReturnsStructuredErrorWithoutLeakingExceptionDetails()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.KnowledgeRetriever.ThrowOnSearch = new KnowledgeEmbeddingException("internal-detail-should-not-leak", retryable: true);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "tiết kiệm 6 tháng" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.RagUnavailable, body.Error?.Code);
        var bodyText = JsonSerializer.Serialize(body);
        Assert.DoesNotContain("internal-detail-should-not-leak", bodyText, StringComparison.Ordinal);
        Assert.DoesNotContain("KnowledgeEmbeddingException", bodyText, StringComparison.Ordinal);
    }

    // --- 14. Raw PII in the message itself — rejected before any Gemini call (D7 Mechanism 1) ---
    [Theory]
    [InlineData("Liên hệ tôi qua minh.anh@example.test nhé")]
    [InlineData("Số điện thoại của tôi là 0900000001")]
    [InlineData("Số tài khoản của tôi là 000000000001")]
    [InlineData("Nhà tôi ở 123 Đường Láng, Phường Láng Hạ, Quận Đống Đa, Hà Nội")]
    public async Task RawPiiInMessage_RejectedWithZeroGeminiCalls(string message)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.PiiRejected, body.Error?.Code);
        Assert.Equal(0, harness.ChatClient.CallCount);
    }

    // --- 15. CRM-oriented message without a customer ID — rejected before any Gemini call (D7 Mechanism 2) ---
    [Theory]
    [InlineData("Cho tôi xem thông tin của Nguyễn Minh Anh")]
    [InlineData("Tìm khách hàng giúp tôi")]
    public async Task CrmOrientedMessageWithoutCustomerId_RejectedWithZeroGeminiCalls(string message)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, body.Error?.Code);
        Assert.Equal(0, harness.ChatClient.CallCount);
    }

    // --- 16. Generic product-knowledge message with no customer reference — allowed through ---
    [Fact]
    public async Task GenericProductKnowledgeMessage_PassesInputGate()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "tiết kiệm 6 tháng" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Sản phẩm PRD-SAV-006M là tiền gửi kỳ hạn 6 tháng."));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
    }

    // --- 17. get_customer called with a name-based query argument — backstop rejection (D7 Mechanism 3) ---
    [Fact]
    public async Task NameBasedGetCustomerArgument_RejectedEvenWhenMessageItselfHasAValidId()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["query"] = "Nguyễn Minh Anh" }));

        // The message itself passes D7 Mechanism 2 (contains a valid CUS-0001 token) — proving
        // this rejection is Mechanism 3 firing independently, not a restatement of Mechanism 2.
        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(ChatTurnErrorCode.NameLookupNotSupported, body.Error?.Code);
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Null(harness.CrmGateway.LastLookupQuery);
    }

    // --- 18. IMcpClientProvider.GetClientAsync throws (D9(a)-2) — no real MCP server needed ---
    [Fact]
    public async Task McpClientProviderHandshakeFailure_ReturnsMcpUnavailable_NoGeminiCall()
    {
        var chatClient = new FakeGeminiChatClient();
        var failingProvider = new FailingMcpClientProvider(new InvalidOperationException("simulated handshake failure"));
        await using var webFactory = WebTestHost.CreateWithDefaults().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGeminiChatClient>();
            services.AddSingleton<IGeminiChatClient>(chatClient);
            services.RemoveAll<IMcpClientProvider>();
            services.AddSingleton<IMcpClientProvider>(failingProvider);
        }));

        var (response, body) = await PostChatAsync(webFactory.CreateClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.McpUnavailable, body.Error?.Code);
        Assert.Equal(0, chatClient.CallCount);
    }

    // --- 19. ListToolsAsync transport failure (D9(b)) — real protocol, controlled failure point ---
    [Fact]
    public async Task ListToolsTransportFailure_ReturnsMcpUnavailable()
    {
        await using var mcpFactory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl);
        var mcpHttpClient = mcpFactory.CreateDefaultClient(new FailOnJsonRpcMethodHandler("tools/list"));
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(mcpHttpClient.BaseAddress!, "mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            mcpHttpClient, ownsHttpClient: true);
        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);

        var chatClient = new FakeGeminiChatClient();
        await using var webFactory = WebTestHost.CreateWithDefaults().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGeminiChatClient>();
            services.AddSingleton<IGeminiChatClient>(chatClient);
            services.RemoveAll<IMcpClientProvider>();
            services.AddSingleton<IMcpClientProvider>(new PreconnectedMcpClientProvider(mcpClient));
        }));

        var (response, body) = await PostChatAsync(webFactory.CreateClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.McpUnavailable, body.Error?.Code);
        Assert.Equal(0, chatClient.CallCount);
    }

    // --- 20. CallToolAsync transport failure (D9(c)) — isolated: ListToolsAsync must succeed first ---
    [Fact]
    public async Task CallToolTransportFailure_ReturnsMcpUnavailable_IsolatedFromListToolsFailure()
    {
        await using var mcpFactory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl);
        var mcpHttpClient = mcpFactory.CreateDefaultClient(new FailOnJsonRpcMethodHandler("tools/call"));
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(mcpHttpClient.BaseAddress!, "mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            mcpHttpClient, ownsHttpClient: true);
        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);

        var chatClient = new FakeGeminiChatClient();
        chatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        await using var webFactory = WebTestHost.CreateWithDefaults().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGeminiChatClient>();
            services.AddSingleton<IGeminiChatClient>(chatClient);
            services.RemoveAll<IMcpClientProvider>();
            services.AddSingleton<IMcpClientProvider>(new PreconnectedMcpClientProvider(mcpClient));
        }));

        var (response, body) = await PostChatAsync(webFactory.CreateClient(), "Tìm khách hàng CUS-0001");

        // ListToolsAsync (not blocked by this handler) must have succeeded — proven by the tool
        // actually being offered/selected at all — before CallToolAsync's own, isolated failure.
        Assert.Equal(1, chatClient.CallCount);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.McpUnavailable, body.Error?.Code);
    }
}
