using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.McpServer.Email;
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

    private static async Task<(HttpResponseMessage Response, ChatResponse Body)> PostChatAsync(
        HttpClient client, string message, string? sessionId = null)
    {
        var effectiveSessionId = sessionId ?? Guid.NewGuid().ToString();
        var response = await client.PostAsJsonAsync(
            "/api/chat", new ChatRequest(message, effectiveSessionId), JsonOptions, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return (response, body!);
    }

    // --- 1. Customer lookup by ID ---
    [Fact]
    public async Task CustomerLookupById_CallsGetCustomer_ReturnsCustomerData()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        // No TextResponse is enqueued: get_customer is terminal (P0-08), so the Host returns
        // immediately and never asks Gemini for a completion. If the early return ever regressed,
        // FakeGeminiChatClient would throw "no more scripted responses" and fail this test loudly.
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal("CUS-0001", body.Data?.Customer?.Id);
        Assert.Contains("crm:customer:CUS-0001", body.SourceIds);
        Assert.Single(body.ToolTrace);
        Assert.Equal("get_customer", body.ToolTrace[0].ToolName);
        Assert.Equal("success", body.ToolTrace[0].Status);
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Equal("Đã tải hồ sơ khách hàng CUS-0001. Xem dữ liệu chi tiết bên dưới.", body.Reply);
    }

    // --- 2. Interaction lookup ---
    [Fact]
    public async Task InteractionLookup_CallsGetInteractions_ReturnsInteractionData()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_interactions", new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["limit"] = 5 }));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Xem các tương tác gần đây của CUS-0001");

        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.NotNull(body.Data?.Interactions);
        Assert.Contains("crm:interaction:INT-0001", body.SourceIds);
        Assert.Equal("CUS-0001", harness.CrmGateway.LastInteractionsCustomerId);
        Assert.Equal(5, harness.CrmGateway.LastInteractionsLimit);
        Assert.Single(body.ToolTrace);
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Equal("Đã tải 1 tương tác gần nhất của khách hàng CUS-0001. Xem dữ liệu chi tiết bên dưới.", body.Reply);
    }

    // --- 3. P0-08 terminal-tool rule: a successful get_customer ends the turn immediately — a
    // second tool the model also asked for is never dispatched, and no second Gemini completion is
    // requested. (Before P0-08 this scenario ran both tools; the rule change is deliberate.) ---
    [Fact]
    public async Task CustomerLookupThenKnowledgeRequest_TerminatesAfterCustomerLookup_SecondToolNeverDispatched()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "gửi tiết kiệm an toàn kỳ hạn 6 tháng" }));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Khách hàng CUS-0001 quan tâm gửi tiết kiệm, gợi ý sản phẩm phù hợp");

        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal("CUS-0001", body.Data?.Customer?.Id);
        Assert.Contains("crm:customer:CUS-0001", body.SourceIds);

        // The turn stopped at the terminal tool: exactly one Gemini call, one trace entry, and the
        // knowledge retriever was never reached despite the model scripting a call to it.
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Single(body.ToolTrace);
        Assert.Equal("get_customer", body.ToolTrace[0].ToolName);
        Assert.Null(harness.KnowledgeRetriever.LastQuery);
        Assert.Null(body.Data?.KnowledgeMatches);

        // The deterministic reply carries no customer PII.
        Assert.NotNull(body.Reply);
        Assert.DoesNotContain(Cus0001.FullName, body.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Email, body.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Phone, body.Reply, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.AccountReference, body.Reply, StringComparison.Ordinal);
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
        Assert.Equal(1, harness.ChatClient.CallCount);
        // P0-08: the deterministic not-found reply names the id that was actually looked up, so the
        // UI can never leave it ambiguous which customer the message refers to.
        Assert.Equal("Không tìm thấy khách hàng CUS-9999.", body.Reply);
        Assert.Null(body.Data);
    }

    // --- 4b. P0-08 live finding: looking up a non-existent id mid-conversation must name that id,
    // must NOT clear the session's active customer, and must not carry the previous customer's data. ---
    [Fact]
    public async Task UnknownCustomerMidConversation_NamesRequestedId_KeepsActiveCustomerForFollowUps()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();
        var client = harness.CreateWebClient();

        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        await PostChatAsync(client, "Tìm hồ sơ khách hàng CUS-0001.", sessionId);

        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.NotFound;
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-9999" }));
        var (response, body) = await PostChatAsync(client, "Tìm khách hàng CUS-9999.", sessionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Không tìm thấy khách hàng CUS-9999.", body.Reply);
        Assert.DoesNotContain("CUS-0001", body.Reply!, StringComparison.Ordinal);
        Assert.Null(body.Data);
        Assert.DoesNotContain(Cus0001.FullName, JsonSerializer.Serialize(body), StringComparison.Ordinal);

        // The failed lookup must not clear the active customer — a follow-up still resolves.
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse("get_interactions"));
        var (followUpResponse, followUpBody) = await PostChatAsync(
            client, "Khách hàng này có tương tác gì gần đây?", sessionId);

        Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, followUpBody.Status);
        Assert.Equal("CUS-0001", harness.CrmGateway.LastInteractionsCustomerId);
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
        Assert.Equal(4, declarations.Count);
        Assert.DoesNotContain(declarations, d => d.Name == "delete_customer");
        Assert.Contains(declarations, d => d.Name == "get_customer");
        Assert.Contains(declarations, d => d.Name == "get_interactions");
        Assert.Contains(declarations, d => d.Name == "search_product_knowledge");
        Assert.Contains(declarations, d => d.Name == "generate_email");
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

    // --- 8. Duplicate identical tool+args. Since P0-08 the three structured CRM tools are terminal
    // (a successful one ends the turn before Gemini could ever repeat it), so the only tool that can
    // still reach this guard is search_product_knowledge — exercised here. Data must still be null
    // on the resulting controlled error. ---
    [Fact]
    public async Task DuplicateToolCall_RejectedOnSecondIdenticalRequest_ErrorResponseCarriesNoAccumulatedData()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "tiết kiệm 6 tháng" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "tiết kiệm 6 tháng" }));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        // Guard still fires — exactly one real MCP call happened (2 Gemini calls: 1st succeeds,
        // 2nd is rejected as a duplicate before ever reaching MCP again).
        Assert.Equal(ChatTurnErrorCode.DuplicateToolCall, body.Error?.Code);
        Assert.Equal(2, harness.ChatClient.CallCount);
        Assert.Single(body.ToolTrace);
        Assert.Null(body.Data);
    }

    // --- 8b. The live P0-05 finding's own scenario (get_customer requested twice) is now
    // structurally unreachable: the first success terminates the turn, so no duplicate can occur
    // and no accumulated CustomerDto can ever ride along in an error response. ---
    [Fact]
    public async Task RepeatedGetCustomerRequest_UnreachableSinceFirstSuccessTerminatesTurn()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Null(body.Error);
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Single(body.ToolTrace);

        // The successful response legitimately carries the CustomerDto for the UI card, but the
        // model-facing side never saw it and the reply itself stays PII-free.
        Assert.Equal("CUS-0001", body.Data?.Customer?.Id);
        Assert.DoesNotContain(Cus0001.FullName, body.Reply!, StringComparison.Ordinal);
    }

    // --- P0-08 supersedes the P0-05 minimized-FunctionResponse behaviour for get_customer: the
    // Host now returns before building any FunctionResponse at all, so no get_customer result — not
    // even the minimized, non-PII one — is ever sent to Gemini. Strictly stronger than the old
    // "minimized payload contains no PII" guarantee it replaces. ---
    [Fact]
    public async Task CustomerLookup_NeverSendsAnyFunctionResponseBackToGemini()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal(1, harness.ChatClient.CallCount);

        // Everything Gemini ever saw this turn — a single call carrying only the user's own message.
        var everythingSentToGemini = JsonSerializer.Serialize(
            harness.ChatClient.CapturedContents.Select(contents => contents.Select(c => c.Parts)));
        Assert.DoesNotContain("functionResponse", everythingSentToGemini, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Cus0001.FullName, everythingSentToGemini, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Email, everythingSentToGemini, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.Phone, everythingSentToGemini, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.AccountReference, everythingSentToGemini, StringComparison.Ordinal);
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

    // --- 10. Always-request-another-tool — bounded at 3 MCP calls, 4th Gemini call still happens.
    // Uses search_product_knowledge throughout: since P0-08 it is the only non-terminal tool, so it
    // is the only one that can drive the loop far enough to reach the bound. ---
    [Fact]
    public async Task AlwaysRequestsAnotherTool_StopsAtThreeMcpCalls_FourthGeminiCallRejectsIt()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);

        foreach (var query in new[] { "truy vấn A", "truy vấn B", "truy vấn C", "truy vấn D" })
        {
            harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
                "search_product_knowledge", new Dictionary<string, object> { ["query"] = query }));
        }

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.Equal(ChatTurnErrorCode.ToolLoopLimitExceeded, body.Error?.Code);
        Assert.Equal(4, harness.ChatClient.CallCount);
        Assert.Equal(3, body.ToolTrace.Count);
    }

    // --- 11. Exactly 3 tools needed, 4th Gemini turn returns final text — legitimate success ---
    [Fact]
    public async Task ExactlyThreeToolsThenFinalText_SucceedsWithFourGeminiCallsThreeMcpCalls()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);

        foreach (var query in new[] { "truy vấn A", "truy vấn B", "truy vấn C" })
        {
            harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
                "search_product_knowledge", new Dictionary<string, object> { ["query"] = query }));
        }
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Đã tổng hợp đầy đủ thông tin."));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal("Đã tổng hợp đầy đủ thông tin.", body.Reply);
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

    private static RawEmailDraftModel ValidRawDraft(string subject = "Thông tin gửi tiết kiệm 6 tháng") => new(
        RawEmailDraftModel.StatusOk,
        subject,
        "Kính gửi {{CUSTOMER_NAME}}, đây là thông tin tham khảo về gửi tiết kiệm 6 tháng.",
        "PRD-SAV-006M",
        ["kb:product:PRD-SAV-006M"],
        true,
        []);

    // --- 21. generate_email (P0-08): canonical success surfaced through /api/chat ---
    [Fact]
    public async Task EmailDraftRequest_CallsGenerateEmail_ReturnsEmailDraftData()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.EmailDraftGenerator.Results.Enqueue(ValidRawDraft());
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "generate_email", new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["objective"] = "Follow-up gửi tiết kiệm 6 tháng" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Soạn email follow-up về tiết kiệm 6 tháng cho khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.NotNull(body.Data?.EmailDraft);
        Assert.True(body.Data!.EmailDraft!.RequiresHumanApproval);
        Assert.Equal("PRD-SAV-006M", body.Data.EmailDraft.SuggestedProductCode);
        Assert.Contains("kb:product:PRD-SAV-006M", body.Data.EmailDraft.SourceIds);
        Assert.Contains(body.ToolTrace, t => t.ToolName == "generate_email" && t.Status == "success");

        // generate_email is terminal: deterministic approval reply, exactly one Gemini call.
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Equal("Đã tạo email nháp cho khách hàng CUS-0001. Bản nháp cần RM kiểm tra và phê duyệt.", body.Reply);
    }

    // --- 22. generate_email: the minimized FunctionResponse must never carry the placeholder-
    // restored customer name or the draft's own subject/body text back into Gemini's context (P0-08 D1). ---
    [Fact]
    public async Task EmailDraftRequest_MinimizedFunctionResponseNeverContainsRestoredCustomerNameOrDraftText()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.EmailDraftGenerator.Results.Enqueue(ValidRawDraft());
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "generate_email", new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["objective"] = "Follow-up gửi tiết kiệm 6 tháng" }));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Soạn email follow-up về tiết kiệm 6 tháng cho khách hàng CUS-0001");

        // Terminal rule: no FunctionResponse is ever built for generate_email, so the restored
        // customer name and the draft's own subject/body never re-enter Gemini's context at all.
        Assert.Equal(1, harness.ChatClient.CallCount);
        var everythingSentToGemini = JsonSerializer.Serialize(
            harness.ChatClient.CapturedContents.Select(contents => contents.Select(c => c.Parts)));
        Assert.DoesNotContain("functionResponse", everythingSentToGemini, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Cus0001.FullName, everythingSentToGemini, StringComparison.Ordinal);
        Assert.DoesNotContain("Thông tin gửi tiết kiệm 6 tháng", everythingSentToGemini, StringComparison.Ordinal);
        Assert.DoesNotContain("đây là thông tin tham khảo", everythingSentToGemini, StringComparison.Ordinal);

        // The deterministic reply likewise carries neither the draft text nor the customer's name.
        Assert.DoesNotContain(Cus0001.FullName, body.Reply!, StringComparison.Ordinal);
        Assert.DoesNotContain("Thông tin gửi tiết kiệm 6 tháng", body.Reply!, StringComparison.Ordinal);
    }

    // --- 23. generate_email without an explicit customerId — resolves from session state
    // (mirrors docs/11 demo script step 3: "...cho khách hàng này", no repeated ID). ---
    [Fact]
    public async Task GenerateEmailWithoutExplicitCustomerId_ResolvesFromSessionState_MatchesDemoStep3()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();
        var client = harness.CreateWebClient();
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);

        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        await PostChatAsync(client, "Tìm khách hàng CUS-0001", sessionId);

        harness.EmailDraftGenerator.Results.Enqueue(ValidRawDraft());
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "generate_email", new Dictionary<string, object> { ["objective"] = "Follow-up tiết kiệm 6 tháng" }));
        var (response, body) = await PostChatAsync(client, "Soạn email follow-up ngắn gọn về tiết kiệm 6 tháng cho khách hàng này", sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body.Data?.EmailDraft);
        // Proves the P0-06 fallback actually reached EmailTools with the resolved id: EmailTools
        // itself calls GetInteractionsAsync(customerId, ...) internally, and this turn's Gemini
        // call omitted customerId entirely, so this can only be "CUS-0001" via session-state resolution.
        Assert.Equal("CUS-0001", harness.CrmGateway.LastInteractionsCustomerId);
    }

    // --- 24. generate_email: RagNoEvidence path — 404 not_found, Data entirely null, Error null,
    // proving the "Data is null on any non-success status" invariant holds for this tool too. ---
    [Fact]
    public async Task GenerateEmail_RagNoEvidence_ReturnsNotFoundWithNullDataAndNullError()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.NoRelevantEvidence;
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "generate_email", new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["objective"] = "Follow-up" }));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Soạn email cho khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ChatTurnStatus.NotFound, body.Status);
        Assert.Null(body.Data);
        Assert.Null(body.Error);

        // RAG no-evidence must NOT reuse the customer-not-found wording — the customer exists here;
        // what is missing is product/template evidence.
        Assert.NotNull(body.Reply);
        Assert.DoesNotContain("Không tìm thấy khách hàng", body.Reply!, StringComparison.Ordinal);
        Assert.Contains("Không đủ dữ liệu sản phẩm", body.Reply!, StringComparison.Ordinal);
    }

    // --- 25. Direct regression for the P0-08 live acceptance failure. Reproduces the exact reported
    // turn-2 shape: after get_interactions succeeds the model asks for a redundant get_customer and
    // would then narrate fabricated content ("Nguyễn Văn An", health insurance). Neither may happen. ---
    [Fact]
    public async Task InteractionsTurn_RedundantFollowUpToolAndHallucinatedNarration_AreBothSuppressed()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();
        var client = harness.CreateWebClient();
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);

        // Turn 1 — establishes CurrentCustomerId.
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        await PostChatAsync(client, "Tìm hồ sơ khách hàng CUS-0001.", sessionId);

        // Turn 2 — the model omits customerId, then (as it did live) asks for a redundant
        // get_customer and finally narrates content it never actually received.
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse("get_interactions"));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse(
            "Khách hàng Nguyễn Văn An quan tâm bảo hiểm sức khỏe và điều trị quốc tế, cần tư vấn lựa chọn gói."));

        var (response, body) = await PostChatAsync(client, "Khách hàng này có những tương tác gần nhất nào?", sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);

        // The turn ended at get_interactions: exactly one trace entry, one Gemini call for this turn
        // (2 total across both turns), and the redundant get_customer never dispatched.
        Assert.Single(body.ToolTrace);
        Assert.Equal("get_interactions", body.ToolTrace[0].ToolName);
        Assert.Equal(2, harness.ChatClient.CallCount);

        // Session state was used, not a model guess.
        Assert.Equal("CUS-0001", harness.CrmGateway.LastInteractionsCustomerId);

        // The hallucinated narration was never adopted.
        Assert.Equal("Đã tải 1 tương tác gần nhất của khách hàng CUS-0001. Xem dữ liệu chi tiết bên dưới.", body.Reply);
        Assert.DoesNotContain("Nguyễn Văn An", body.Reply!, StringComparison.Ordinal);
        Assert.DoesNotContain("bảo hiểm", body.Reply!, StringComparison.Ordinal);
        Assert.DoesNotContain(Cus0001.FullName, body.Reply!, StringComparison.Ordinal);
        Assert.DoesNotContain(Int0001.Summary, body.Reply!, StringComparison.Ordinal);

        // Structured data stays complete and accurate — the cards remain the data surface.
        Assert.NotNull(body.Data?.Interactions);
        Assert.Equal("INT-0001", body.Data!.Interactions![0].Id);
        Assert.Equal(Int0001.Summary, body.Data.Interactions[0].Summary);
        Assert.Contains("crm:interaction:INT-0001", body.SourceIds);
    }

    // --- 27. P0-08 live turn-3 failure: Gemini emitted search_product_knowledge AND generate_email
    // in one batch, which the parallel-call guard rejected outright. generate_email already does its
    // own nested retrieval, so exactly that batch collapses to generate_email alone — in either
    // order — and the outer search is never dispatched. ---
    [Theory]
    [InlineData(true)]  // [search_product_knowledge, generate_email]
    [InlineData(false)] // [generate_email, search_product_knowledge]
    public async Task Batch_SearchAndGenerateEmail_CollapsesToGenerateEmailOnly(bool searchFirst)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.EmailDraftGenerator.Results.Enqueue(ValidRawDraft());

        var searchCall = ("search_product_knowledge",
            new Dictionary<string, object> { ["query"] = "gửi tiết kiệm 6 tháng" });
        var emailCall = ("generate_email",
            new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["objective"] = "Follow-up gửi tiết kiệm 6 tháng" });

        harness.ChatClient.Enqueue(searchFirst
            ? FakeGeminiChatClient.MultiFunctionCallResponse(searchCall, emailCall)
            : FakeGeminiChatClient.MultiFunctionCallResponse(emailCall, searchCall));

        var (response, body) = await PostChatAsync(
            harness.CreateWebClient(),
            "Soạn email follow-up ngắn gọn, chuyên nghiệp về nhu cầu gửi tiết kiệm 6 tháng cho khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Null(body.Error);

        // Exactly one MCP dispatch, and it is generate_email — the outer search never ran.
        Assert.Single(body.ToolTrace);
        Assert.Equal("generate_email", body.ToolTrace[0].ToolName);
        Assert.Equal("success", body.ToolTrace[0].Status);
        Assert.DoesNotContain(body.ToolTrace, t => t.ToolName == "search_product_knowledge");
        // If the outer search had been dispatched, MergeData would have populated this.
        Assert.Null(body.Data?.KnowledgeMatches);
        Assert.Equal(1, harness.ChatClient.CallCount);

        // Draft data is complete.
        Assert.NotNull(body.Data?.EmailDraft);
        Assert.True(body.Data!.EmailDraft!.RequiresHumanApproval);
        Assert.Equal("PRD-SAV-006M", body.Data.EmailDraft.SuggestedProductCode);
        Assert.Contains("kb:product:PRD-SAV-006M", body.Data.EmailDraft.SourceIds);
        Assert.Contains("kb:product:PRD-SAV-006M", body.SourceIds);

        // Deterministic, PII-safe reply.
        Assert.Equal("Đã tạo email nháp cho khách hàng CUS-0001. Bản nháp cần RM kiểm tra và phê duyệt.", body.Reply);
        Assert.DoesNotContain(Cus0001.FullName, body.Reply!, StringComparison.Ordinal);
        Assert.DoesNotContain("Thông tin gửi tiết kiệm 6 tháng", body.Reply!, StringComparison.Ordinal);
        Assert.DoesNotContain("đây là thông tin tham khảo", body.Reply!, StringComparison.Ordinal);
    }

    // --- 28. The exact live turn-3 shape: the collapsed generate_email still gets CurrentCustomerId
    // backfilled from session state when the model omits it ("cho khách hàng này"). ---
    [Fact]
    public async Task Batch_SearchAndGenerateEmail_WithoutCustomerId_BackfillsFromSessionState()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();
        var client = harness.CreateWebClient();
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);

        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        await PostChatAsync(client, "Tìm hồ sơ khách hàng CUS-0001.", sessionId);

        harness.EmailDraftGenerator.Results.Enqueue(ValidRawDraft());
        harness.ChatClient.Enqueue(FakeGeminiChatClient.MultiFunctionCallResponse(
            ("search_product_knowledge", new Dictionary<string, object> { ["query"] = "gửi tiết kiệm 6 tháng" }),
            ("generate_email", new Dictionary<string, object> { ["objective"] = "Follow-up gửi tiết kiệm 6 tháng" })));

        var (response, body) = await PostChatAsync(
            client,
            "Soạn email follow-up ngắn gọn, chuyên nghiệp và thân thiện về nhu cầu gửi tiết kiệm 6 tháng cho khách hàng này.",
            sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Single(body.ToolTrace);
        Assert.Equal("generate_email", body.ToolTrace[0].ToolName);
        Assert.NotNull(body.Data?.EmailDraft);
        Assert.Equal("Đã tạo email nháp cho khách hàng CUS-0001. Bản nháp cần RM kiểm tra và phê duyệt.", body.Reply);
        // EmailTools resolves interactions for the backfilled id — proves the id reached the tool.
        Assert.Equal("CUS-0001", harness.CrmGateway.LastInteractionsCustomerId);
    }

    // --- 29. The collapse rule is deliberately narrow: every other multi-call batch is still
    // rejected outright, with no MCP dispatch at all. ---
    [Theory]
    [InlineData("get_customer", "get_interactions")]
    [InlineData("get_customer", "generate_email")]
    [InlineData("generate_email", "generate_email")]
    [InlineData("search_product_knowledge", "search_product_knowledge")]
    [InlineData("get_interactions", "generate_email")]
    public async Task Batch_OtherMultiCallCombinations_StillRejectedWithoutAnyMcpCall(string firstTool, string secondTool)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.EmailDraftGenerator.Results.Enqueue(ValidRawDraft());

        var args = new Dictionary<string, object> { ["customerId"] = "CUS-0001", ["objective"] = "Follow-up", ["query"] = "tiết kiệm" };
        harness.ChatClient.Enqueue(FakeGeminiChatClient.MultiFunctionCallResponse((firstTool, args), (secondTool, args)));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Soạn email cho khách hàng CUS-0001");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.MultipleFunctionCallsNotSupported, body.Error?.Code);
        Assert.Empty(body.ToolTrace);
        Assert.Null(body.Data);
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Null(harness.CrmGateway.LastLookupQuery);
        Assert.Equal(0, harness.EmailDraftGenerator.CallCount);
    }

    // --- 26. Guards against over-correcting: a knowledge-only turn has genuinely grounded evidence
    // in Gemini's context, so the model's own prose must still be used verbatim. ---
    [Fact]
    public async Task KnowledgeOnlyTurn_StillUsesGroundedGeminiProse()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "search_product_knowledge", new Dictionary<string, object> { ["query"] = "tiết kiệm 6 tháng" }));
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse(
            "Sản phẩm PRD-SAV-006M là tiền gửi kỳ hạn 6 tháng dành cho khách hàng ưu tiên an toàn."));

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal(
            "Sản phẩm PRD-SAV-006M là tiền gửi kỳ hạn 6 tháng dành cho khách hàng ưu tiên an toàn.", body.Reply);
        Assert.Equal(2, harness.ChatClient.CallCount);
        Assert.NotNull(body.Data?.KnowledgeMatches);
    }
}
