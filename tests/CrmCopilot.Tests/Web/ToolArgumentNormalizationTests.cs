using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Email;
using CrmCopilot.Tests.Web.TestSupport;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// Regression cover for the browser-verified P0-10 defect: asked in plain Vietnamese ("Soạn email
/// follow-up cho khách hàng này về gửi tiết kiệm 6 tháng"), Gemini filled <c>productCode</c> with
/// the natural-language phrase, and the MCP tool correctly rejected it — so a reasonable request
/// failed while the identical request phrased with "PRD-SAV-006M" succeeded.
///
/// The fix is Host-side and deliberately one-directional: a malformed identifier is DROPPED before
/// dispatch, never repaired, never forwarded. These tests pin both halves — the Host stops sending
/// junk, AND the MCP validator stays exactly as strict for anyone calling it directly.
/// </summary>
public class ToolArgumentNormalizationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string NaturalLanguageProductPhrase = "gửi tiết kiệm 6 tháng";

    private static readonly CustomerDto Cus0001 = new(
        "CUS-0001", "Nguyễn Minh Anh", "minh.anh@example.test", "0900000001", "000000000001",
        "Priority", "Hà Nội", "vi", "RM-001", "Active", true, DateTime.UtcNow);

    private static readonly InteractionDto Int0001 =
        new("INT-0001", "CUS-0001", "Call", DateTime.UtcNow, "Quan tâm gửi tiết kiệm.", "FollowUpRequired", null, true);

    private static KnowledgeMatch SavingsMatch() => new(
        "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "Tiền gửi kỳ hạn sáu tháng.",
        new KnowledgeSourceMetadata("kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
            "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp"),
        Distance: 0.47);

    private static RawEmailDraftModel ValidRawDraft() => new(
        RawEmailDraftModel.StatusOk,
        "Thông tin gửi tiết kiệm 6 tháng",
        "Kính gửi {{CUSTOMER_NAME}},\n\nĐây là thông tin tham khảo.\n\n"
        + "Mong Anh/Chị phản hồi thời gian phù hợp.\n\nTrân trọng,",
        "PRD-SAV-006M",
        ["kb:product:PRD-SAV-006M"],
        true,
        []);

    private static async Task<(HttpResponseMessage Response, ChatResponse Body)> PostChatAsync(
        HttpClient client, string message, string? sessionId = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/chat", new ChatRequest(message, sessionId ?? Guid.NewGuid().ToString()), JsonOptions,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, TestContext.Current.CancellationToken);
        return (response, body!);
    }

    /// <summary>
    /// Reproduces the browser flow exactly: turn 1 establishes the session's active customer, turn 2
    /// says "khách hàng này". Phrased that way in a fresh session, InputGuard would reject the
    /// message before any tool call — correctly, and for an unrelated reason — so a single-turn test
    /// could never reach the productCode defect at all.
    /// </summary>
    private static async Task<(HttpResponseMessage Response, ChatResponse Body)> RunEstablishThenEmailAsync(
        ChatTestHarness harness, Dictionary<string, object> emailArgs, string emailMessage)
    {
        var client = harness.CreateWebClient();
        var sessionId = Guid.NewGuid().ToString();

        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = "CUS-0001" }));
        var (_, firstTurn) = await PostChatAsync(client, "Tìm khách hàng CUS-0001", sessionId);
        Assert.Equal(ChatTurnStatus.Success, firstTurn.Status);

        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse("generate_email", emailArgs));
        return await PostChatAsync(client, emailMessage, sessionId);
    }

    private static async Task<ChatTestHarness> CreateEmailHarnessAsync()
    {
        var harness = await ChatTestHarness.CreateAsync(TestContext.Current.CancellationToken);
        harness.CrmGateway.FindCustomerResult = CustomerLookupResult.Found(Cus0001);
        harness.CrmGateway.InteractionsResult = [Int0001];
        harness.KnowledgeRetriever.SearchResult = KnowledgeSearchResult.Found([SavingsMatch()]);
        harness.EmailDraftGenerator.Results.Enqueue(ValidRawDraft());
        return harness;
    }

    /// <summary>Acceptance 1: the natural-language phrasing must now succeed end to end.</summary>
    [Fact]
    public async Task NaturalLanguageProductCode_IsDroppedAndTheTurnSucceeds()
    {
        await using var harness = await CreateEmailHarnessAsync();

        var (response, body) = await RunEstablishThenEmailAsync(
            harness,
            new Dictionary<string, object>
            {
                ["customerId"] = "CUS-0001",
                ["objective"] = "Follow-up nhu cầu tiền gửi",
                ["productCode"] = NaturalLanguageProductPhrase,
            },
            "Soạn email follow-up cho khách hàng này về gửi tiết kiệm 6 tháng");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.NotNull(body.Data?.EmailDraft);

        // The tool must have been invoked with NO requested product code at all — not a repaired one.
        Assert.Null(harness.EmailDraftGenerator.LastContext!.RequestedProductCode);
    }

    /// <summary>The dropped phrase is not discarded — it is folded into the objective, so the
    /// tool's own retrieval still gets the signal it needs to find the right product.</summary>
    [Fact]
    public async Task DroppedProductCode_IsPreservedInTheObjective()
    {
        await using var harness = await CreateEmailHarnessAsync();

        await RunEstablishThenEmailAsync(
            harness,
            new Dictionary<string, object>
            {
                ["customerId"] = "CUS-0001",
                ["objective"] = "Follow-up nhu cầu tiền gửi",
                ["productCode"] = NaturalLanguageProductPhrase,
            },
            "Soạn email follow-up cho khách hàng này về gửi tiết kiệm 6 tháng");

        var maskedObjective = harness.EmailDraftGenerator.LastContext!.MaskedObjective;
        Assert.Contains("Follow-up nhu cầu tiền gửi", maskedObjective, StringComparison.Ordinal);
        Assert.Contains(NaturalLanguageProductPhrase, maskedObjective, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DroppedProductCode_WithNoObjective_BecomesTheObjective()
    {
        await using var harness = await CreateEmailHarnessAsync();

        var (_, body) = await RunEstablishThenEmailAsync(
            harness,
            new Dictionary<string, object>
            {
                ["customerId"] = "CUS-0001",
                ["productCode"] = NaturalLanguageProductPhrase,
            },
            "Soạn email cho khách hàng này về gửi tiết kiệm");

        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Contains(
            NaturalLanguageProductPhrase,
            harness.EmailDraftGenerator.LastContext!.MaskedObjective,
            StringComparison.Ordinal);
    }

    /// <summary>Acceptance 2: a well-formed code is passed through untouched — the normalizer must
    /// not become a blanket "strip productCode" rule.</summary>
    [Fact]
    public async Task WellFormedProductCode_ReachesTheToolUnchanged()
    {
        await using var harness = await CreateEmailHarnessAsync();
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "generate_email",
            new Dictionary<string, object>
            {
                ["customerId"] = "CUS-0001",
                ["objective"] = "Follow-up",
                ["productCode"] = "PRD-SAV-006M",
            }));

        var (_, body) = await PostChatAsync(
            harness.CreateWebClient(), "Soạn email follow-up cho khách hàng CUS-0001 về sản phẩm PRD-SAV-006M");

        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Equal("PRD-SAV-006M", harness.EmailDraftGenerator.LastContext!.RequestedProductCode);
    }

    [Theory]
    [InlineData("prd-sav-006m")]      // wrong case
    [InlineData("PRD")]                // too few segments
    [InlineData("Tiền gửi 6 tháng")]  // natural language, accented
    [InlineData("sản phẩm tiết kiệm")]
    public async Task AnyMalformedProductCode_IsDropped(string malformed)
    {
        await using var harness = await CreateEmailHarnessAsync();
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "generate_email",
            new Dictionary<string, object>
            {
                ["customerId"] = "CUS-0001",
                ["objective"] = "Follow-up",
                ["productCode"] = malformed,
            }));

        var (_, body) = await PostChatAsync(harness.CreateWebClient(), "Soạn email cho khách hàng CUS-0001");

        Assert.Equal(ChatTurnStatus.Success, body.Status);
        Assert.Null(harness.EmailDraftGenerator.LastContext!.RequestedProductCode);
    }
}
