using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Tests.Crm.TestSupport;
using CrmCopilot.Tests.Web.TestSupport;
using CrmCopilot.Web.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// P0-06 end-to-end coverage (plan, docs/04 §P0-06 pass evidence): conversation-state resolution of
/// "khách hàng này"-style follow-ups, session isolation, clarification-on-no-active-customer,
/// reset, session ID validation, and the newest-8 message cap — all through a real
/// <see cref="ChatTestHarness"/> (real MCP client/server, only Gemini/MCP-client-provider faked),
/// exactly like <see cref="ChatEndpointTests"/>.
/// </summary>
public class ConversationStateEndToEndTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly CustomerDto Cus0001 = new(
        "CUS-0001", "Nguyễn Minh Anh", "minh.anh@example.test", "0900000001", "000000000001",
        "Priority", "Hà Nội", "vi", "RM-001", "Active", true, DateTime.UtcNow);

    private static readonly CustomerDto Cus0002 = new(
        "CUS-0002", "Trần Thu Hà", "thu.ha@example.test", "0900000002", "000000000002",
        "Standard", "Hồ Chí Minh", "vi", "RM-001", "Active", true, DateTime.UtcNow);

    private static readonly InteractionDto Int0001 = new(
        "INT-0001", "CUS-0001", "Call", DateTime.UtcNow,
        "Khách hàng quan tâm tiền gửi kỳ hạn 6 tháng.", "FollowUpRequired", null, true);

    private static readonly InteractionDto Int0002 = new(
        "INT-0002", "CUS-0002", "Email", DateTime.UtcNow,
        "Khách hàng hỏi về lãi suất vay tiêu dùng.", "FollowUpRequired", null, true);

    private static async Task<(HttpResponseMessage Response, ChatResponse Body)> PostChatAsync(
        HttpClient client, string message, string sessionId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/chat", new ChatRequest(message, sessionId), JsonOptions, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return (response, body!);
    }

    /// <summary>P0-12: the store is keyed by (userId, sessionId); these tests pin one user.</summary>
    private const string TestUserId = "rm01";

    private static IConversationStateStore GetStateStore(ChatTestHarness harness) =>
        harness.WebFactory.Services.GetRequiredService<IConversationStateStore>();

    // --- Turn 1 lookup updates CurrentCustomerId ---
    [Fact]
    public async Task Turn1_GetCustomer_UpdatesCurrentCustomerId()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        // Since P0-08 get_customer is terminal — no follow-up Gemini completion is requested, so no
        // TextResponse is scripted here (an unconsumed one would leak into the next turn's queue).
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));

        var (response, _) = await PostChatAsync(harness.CreateWebClient(), "Tìm khách hàng CUS-0001", sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CUS-0001", GetStateStore(harness).GetOrCreate(TestUserId, sessionId).CurrentCustomerId);
        Assert.Equal(1, harness.ChatClient.CallCount);
    }

    // --- Turn 2 "khách hàng này" reuses the stored ID, deterministically ---
    [Fact]
    public async Task Turn2_FollowUp_ResolvesToStoredCustomerId_EvenWhenGeminiOmitsIt()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        var client = harness.CreateWebClient();
        await PostChatAsync(client, "Tìm khách hàng CUS-0001", sessionId);

        harness.CrmGateway.InteractionsResult = [Int0001];
        // Deliberately omit customerId — proves the Host substitutes it, not that Gemini guessed.
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse("get_interactions"));

        var (response, body) = await PostChatAsync(client, "Khách hàng này có tương tác gì gần đây?", sessionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal("CUS-0001", harness.CrmGateway.LastInteractionsCustomerId);
        Assert.Single(body.ToolTrace);
    }

    // --- Switching the active customer mid-session ---
    [Fact]
    public async Task SwitchingCustomer_FollowUpUsesMostRecentlyResolvedCustomer()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();
        var client = harness.CreateWebClient();

        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        await PostChatAsync(client, "Tìm khách hàng CUS-0001", sessionId);

        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0002);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0002" }));
        await PostChatAsync(client, "Tìm khách hàng CUS-0002", sessionId);

        Assert.Equal("CUS-0002", GetStateStore(harness).GetOrCreate(TestUserId, sessionId).CurrentCustomerId);

        harness.CrmGateway.InteractionsResult = [Int0002];
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse("get_interactions"));
        await PostChatAsync(client, "Khách hàng này có tương tác gì gần đây?", sessionId);

        Assert.Equal("CUS-0002", harness.CrmGateway.LastInteractionsCustomerId);
    }

    // --- Two sessions don't leak state ---
    [Fact]
    public async Task TwoSessions_DoNotShareState()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var client = harness.CreateWebClient();

        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        await PostChatAsync(client, "Tìm khách hàng CUS-0001", sessionA);

        var (response, body) = await PostChatAsync(client, "Khách hàng này có tương tác gì?", sessionB);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, body.Error?.Code);
        // Session A's turn made exactly 1 Gemini call (the terminal get_customer ends the turn with
        // no follow-up completion); session B's turn is rejected by InputGuard before Gemini is ever
        // reached, so the count doesn't grow.
        Assert.Equal(1, harness.ChatClient.CallCount);
        Assert.Null(GetStateStore(harness).GetOrCreate(TestUserId, sessionB).CurrentCustomerId);
    }

    // --- Follow-up with no active customer asks for clarification ---
    [Fact]
    public async Task FollowUp_NoActiveCustomer_AsksForClarification_NoGeminiOrMcpCall()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();

        var (response, body) = await PostChatAsync(
            harness.CreateWebClient(), "Khách hàng này có tương tác gì gần đây?", sessionId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Error, body.Status);
        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, body.Error?.Code);
        Assert.Equal(0, harness.ChatClient.CallCount);
    }

    // --- Reset works and is session-scoped ---
    [Fact]
    public async Task Reset_ClearsStoredCustomerId_ButNotOtherSessions()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var client = harness.CreateWebClient();

        foreach (var (sessionId, customer) in new[] { (sessionA, Cus0001), (sessionB, Cus0002) })
        {
            harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(customer);
            harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
                "get_customer", new Dictionary<string, object> { ["customerId"] = customer.Id }));
            await PostChatAsync(client, $"Tìm khách hàng {customer.Id}", sessionId);
        }

        var deleteResponse = await client.DeleteAsync(
            $"/api/chat/sessions/{sessionA}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Null(GetStateStore(harness).GetOrCreate(TestUserId, sessionA).CurrentCustomerId);
        Assert.Equal("CUS-0002", GetStateStore(harness).GetOrCreate(TestUserId, sessionB).CurrentCustomerId);

        var (response, body) = await PostChatAsync(client, "Khách hàng này có tương tác gì?", sessionA);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, body.Error?.Code);
    }

    [Fact]
    public async Task Reset_WellFormedGuidNeverUsed_ReturnsNoContent()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);

        var response = await harness.CreateWebClient().DeleteAsync(
            $"/api/chat/sessions/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Session ID validation, shared by both endpoints ---
    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task Chat_InvalidSessionId_ReturnsInvalidArgument_NoGeminiCall(string sessionId)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);

        var (response, body) = await PostChatAsync(harness.CreateWebClient(), "Xin chào", sessionId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.InvalidArgument, body.Error?.Code);
        Assert.Equal(0, harness.ChatClient.CallCount);
    }

    [Fact]
    public async Task Reset_MalformedSessionId_ReturnsInvalidArgument()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);

        var response = await harness.CreateWebClient().DeleteAsync(
            "/api/chat/sessions/not-a-guid", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChatTurnError>(JsonOptions, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ChatTurnErrorCode.InvalidArgument, body?.Code);
    }

    // --- RecentSanitizedMessages keeps only the newest 8 ---
    [Fact]
    public async Task RecentSanitizedMessages_KeepsOnlyNewest8()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var sessionId = Guid.NewGuid().ToString();
        var client = harness.CreateWebClient();

        for (var i = 1; i <= 9; i++)
        {
            harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse($"Phản hồi số {i}."));
            await PostChatAsync(client, $"Câu hỏi số {i} về sản phẩm tiết kiệm", sessionId);
        }

        var messages = GetStateStore(harness).GetOrCreate(TestUserId, sessionId).RecentSanitizedMessages;
        Assert.Equal(8, messages.Count);
        Assert.DoesNotContain("Câu hỏi số 1 về sản phẩm tiết kiệm", messages);
        Assert.Contains("Câu hỏi số 9 về sản phẩm tiết kiệm", messages);
    }
}
