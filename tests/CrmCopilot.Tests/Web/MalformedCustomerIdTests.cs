using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Tests.Web.TestSupport;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// Browser-verified regression suite for malformed customer identifiers reaching /api/chat.
///
/// Three distinct wrong behaviours were observed from the same root cause — InputGuard letting a
/// customer-id-shaped typo through to Gemini:
///
///   CS-0002 → the model substituted the session's customer and the turn reported success for
///             CUS-0002. Wrong customer, presented as a win.
///   CS-0003 → forwarded as a lookup and answered NOT_FOUND, which asserts the id was well-formed
///             and merely absent. It was not.
///   CS-0004 → treated as a name query, then the Host injected the session customerId beside it, so
///             get_customer refused the call and its internal validator message surfaced to the RM.
///
/// All three must now be one deterministic outcome: CUSTOMER_ID_INVALID, before any tool call.
/// </summary>
public class MalformedCustomerIdTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string EstablishedCustomerId = "CUS-0002";

    private static readonly CustomerDto Cus0002 = new(
        EstablishedCustomerId, "Trần Thị Hương", "huong@example.test", "0900000002", "000000000002",
        "Priority", "Đà Nẵng", "vi", "RM-002", "Active", true, DateTime.UtcNow);

    /// <summary>The exact inputs reproduced in the browser.</summary>
    public static TheoryData<string> MalformedMessages() =>
    [
        "Tra cứu khách hàng CS-0002",
        "Tra cứu khách hàng CS-0003",
        "Tra cứu khách hàng CS-0004",
    ];

    private static async Task<(HttpResponseMessage Response, ChatResponse Body)> PostChatAsync(
        HttpClient client, string message, string sessionId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/chat", new ChatRequest(message, sessionId), JsonOptions, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return (response, body!);
    }

    /// <summary>Runs a first turn that establishes CUS-0002 as the session's active customer.</summary>
    private static async Task EstablishSessionCustomerAsync(ChatTestHarness harness, HttpClient client, string sessionId)
    {
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0002);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = EstablishedCustomerId }));

        var (_, body) = await PostChatAsync(client, $"Tìm khách hàng {EstablishedCustomerId}", sessionId);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal(EstablishedCustomerId, body.Data?.Customer?.Id);
    }

    private static void AssertRejectedWithoutAnyToolCall(ChatResponse body, ChatTestHarness harness, int expectedGeminiCalls)
    {
        Assert.Equal(ChatTurnStatus.Error, body.Status);
        Assert.Equal(ChatTurnErrorCode.CustomerIdInvalid, body.Error?.Code);
        Assert.NotEqual(ChatTurnErrorCode.NotFound, body.Error?.Code);
        Assert.False(body.Error!.Retryable);
        Assert.Equal("Mã khách hàng không hợp lệ. Vui lòng kiểm tra đúng định dạng và thử lại.", body.Error.Message);

        // Nothing was dispatched: no MCP tool call, and Gemini was never asked in the first place.
        Assert.Empty(body.ToolTrace);
        Assert.Null(body.Data);
        Assert.Equal(expectedGeminiCalls, harness.ChatClient.CallCount);

        AssertPublicMessageLeaksNoImplementationDetail(body.Error.Message);
    }

    /// <summary>
    /// The public error is the whole surface an end user sees, so it must carry no implementation
    /// detail: not the id convention (which would hand out the shape of every valid customer key),
    /// not an internal error code, not the tool's own validator wording, not a type or stack frame.
    /// </summary>
    private static void AssertPublicMessageLeaksNoImplementationDetail(string publicMessage)
    {
        Assert.DoesNotContain("CUS-", publicMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("####", publicMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\d", publicMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("regex", publicMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INVALID_ARGUMENT", publicMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("CUSTOMER_ID_INVALID", publicMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Chỉ được cung cấp một trong", publicMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", publicMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CrmCopilot.", publicMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", publicMessage, StringComparison.Ordinal);
    }

    /// <summary>The internal code is unchanged — only the human-readable text was generalized.</summary>
    [Fact]
    public async Task MalformedCustomerId_StillReportsTheInternalCodeUnchanged()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);

        var (_, body) = await PostChatAsync(
            harness.CreateWebClient(), "Tra cứu khách hàng CS-0003", Guid.NewGuid().ToString());

        Assert.Equal("CUSTOMER_ID_INVALID", body.Error?.Code);
        AssertPublicMessageLeaksNoImplementationDetail(body.Error!.Message);
    }

    // ---- fresh session --------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(MalformedMessages))]
    public async Task FreshSession_MalformedCustomerId_IsRejectedBeforeAnyToolCall(string message)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var client = harness.CreateWebClient();

        var (response, body) = await PostChatAsync(client, message, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertRejectedWithoutAnyToolCall(body, harness, expectedGeminiCalls: 0);
        Assert.Null(harness.CrmGateway.LastLookupQuery);
    }

    [Fact]
    public async Task FreshSession_MalformedCustomerId_EstablishesNoCustomerSessionState()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var client = harness.CreateWebClient();
        var sessionId = Guid.NewGuid().ToString();

        await PostChatAsync(client, "Tra cứu khách hàng CS-0002", sessionId);

        // If any customer had been recorded, this follow-up would resolve instead of asking for one.
        var (_, followUp) = await PostChatAsync(client, "Xem tương tác của khách hàng này", sessionId);

        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, followUp.Error?.Code);
        Assert.Null(harness.CrmGateway.LastLookupQuery);
    }

    // ---- existing session holding CUS-0002 -------------------------------------------------------

    [Theory]
    [MemberData(nameof(MalformedMessages))]
    public async Task ExistingSession_MalformedCustomerId_NeverFallsBackToTheSessionCustomer(string message)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var client = harness.CreateWebClient();
        var sessionId = Guid.NewGuid().ToString();
        await EstablishSessionCustomerAsync(harness, client, sessionId);

        var geminiCallsAfterSetup = harness.ChatClient.CallCount;
        harness.CrmGateway.ResetCallTracking();

        var (response, body) = await PostChatAsync(client, message, sessionId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertRejectedWithoutAnyToolCall(body, harness, geminiCallsAfterSetup);

        // The decisive assertion: get_customer was not called again, so nothing silently resolved to
        // the session's CUS-0002 and reported success.
        Assert.Null(harness.CrmGateway.LastLookupQuery);
    }

    [Theory]
    [MemberData(nameof(MalformedMessages))]
    public async Task ExistingSession_MalformedCustomerId_LeavesTheValidCustomerContextIntact(string message)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var client = harness.CreateWebClient();
        var sessionId = Guid.NewGuid().ToString();
        await EstablishSessionCustomerAsync(harness, client, sessionId);

        await PostChatAsync(client, message, sessionId);

        // A valid follow-up afterwards must still resolve against CUS-0002 — the rejected turn must
        // neither clear nor overwrite the session's customer.
        harness.CrmGateway.InteractionsResult =
            [new InteractionDto("INT-0100", EstablishedCustomerId, "Call", DateTime.UtcNow, "s", "o", null, true)];
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse("get_interactions", []));

        var (_, followUp) = await PostChatAsync(client, "Xem tương tác của khách hàng này", sessionId);

        Assert.Equal(ChatTurnStatus.Success, followUp.Status);
        Assert.Equal(EstablishedCustomerId, harness.CrmGateway.LastInteractionsCustomerId);
    }

    /// <summary>
    /// Case 3 specifically: the Host must never build a call carrying both customerId and query.
    /// Asserted at the orchestrator level by driving the model to emit a bare `query` while the
    /// session holds a customer — the shape that previously produced the mixed-argument call.
    /// </summary>
    [Fact]
    public async Task ExistingSession_ModelEmitsBareQuery_HostNeverCombinesItWithTheSessionCustomerId()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var client = harness.CreateWebClient();
        var sessionId = Guid.NewGuid().ToString();
        await EstablishSessionCustomerAsync(harness, client, sessionId);
        harness.CrmGateway.ResetCallTracking();

        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["query"] = "Trần Thị Hương" }));

        var (_, body) = await PostChatAsync(client, "Tra cứu hồ sơ khách hàng này giúp tôi", sessionId);

        // Name lookup through chat stays refused by the existing P0-05 rule — and crucially the
        // refusal is NAME_LOOKUP_NOT_SUPPORTED, not the tool's mixed-argument complaint.
        Assert.Equal(ChatTurnErrorCode.NameLookupNotSupported, body.Error?.Code);
        Assert.DoesNotContain("Chỉ được cung cấp một trong", body.Error!.Message, StringComparison.Ordinal);
        Assert.Null(harness.CrmGateway.LastLookupQuery);
    }

    // ---- non-regression --------------------------------------------------------------------------

    [Fact]
    public async Task WellFormedCustomerId_StillResolvesSuccessfully()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        var client = harness.CreateWebClient();

        await EstablishSessionCustomerAsync(harness, client, Guid.NewGuid().ToString());

        Assert.Equal(EstablishedCustomerId, harness.CrmGateway.LastLookupQuery?.CustomerId);
    }

    [Fact]
    public async Task WellFormedButNonexistentCustomerId_StillReturnsNotFound()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.NotFound;
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-9999" }));

        var (response, body) = await PostChatAsync(
            harness.CreateWebClient(), "Tìm khách hàng CUS-9999", Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ChatTurnStatus.NotFound, body.Status);
        Assert.Equal(ChatTurnErrorCode.NotFound, body.Error?.Code);
        // A well-formed id that is simply absent must NOT be reclassified as malformed.
        Assert.NotEqual(ChatTurnErrorCode.CustomerIdInvalid, body.Error?.Code);
    }

    /// <summary>
    /// The other identifier families a user may legitimately mention must not be mistaken for a
    /// malformed customer id — the new guard is narrow on purpose.
    /// </summary>
    [Theory]
    [InlineData("Khách hàng CUS-0002 có cơ hội OPP-0002 nào không?")]
    [InlineData("Xem tương tác INT-0001 của khách hàng CUS-0002")]
    [InlineData("Chiến dịch CMP-0001 của khách hàng CUS-0002")]
    [InlineData("Soạn email cho khách hàng CUS-0002 về sản phẩm PRD-SAV-006M")]
    public async Task OtherIdentifierFamilies_AreNotTreatedAsMalformedCustomerIds(string message)
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.ChatClient.Enqueue(FakeGeminiChatClient.TextResponse("Vâng, tôi đã ghi nhận."));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), message, Guid.NewGuid().ToString());

        Assert.NotEqual(ChatTurnErrorCode.CustomerIdInvalid, body.Error?.Code);
    }

    [Fact]
    public async Task FreshSession_ThisCustomerPhrase_StillAsksForAnId()
    {
        await using var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);

        var (_, body) = await PostChatAsync(
            harness.CreateWebClient(), "Xem tương tác của khách hàng này", Guid.NewGuid().ToString());

        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, body.Error?.Code);
        Assert.Equal(0, harness.ChatClient.CallCount);
    }
}
