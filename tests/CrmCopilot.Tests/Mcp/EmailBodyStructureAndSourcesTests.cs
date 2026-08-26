using System.Text.Json;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Email;
using CrmCopilot.Tests.Crm.TestSupport;
using CrmCopilot.Tests.Email.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.Mcp;

/// <summary>
/// P0-10 browser-verified follow-ups for generate_email:
///
/// B — the draft body must read as a real email: greeting, intro, product content, call to action,
///     closing, separated by blank lines, in plain text. Before this, drafts arrived as a single
///     undifferentiated block.
/// C — the reported sources must be the ones the email was actually built from: never a call-script
///     playbook (a different tool's evidence type), never a retrieved product the draft did not use.
/// </summary>
public class EmailBodyStructureAndSourcesTests
{
    private static readonly CustomerDto Customer = new(
        "CUS-0001", "Nguyễn Minh Anh", "minh.anh@example.test", "0900000001", "000000000001",
        "Priority", "Hà Nội", "vi", "RM-0001", "Active", true, DateTime.UtcNow);

    private static KnowledgeMatch Product(string code, string content, double distance) => new(
        $"kb:product:{code}", KnowledgeDocumentType.Product, content,
        new KnowledgeSourceMetadata($"kb:product:{code}", KnowledgeDocumentType.Product, code, null,
            "vi", "1.0", "gemini-embedding-001", 768, "l2", true, $"fp-{code}"),
        distance);

    private static readonly KnowledgeMatch ProductUsed = Product("PRD-SAV-006M", "nội dung sản phẩm dùng", 0.4);
    private static readonly KnowledgeMatch ProductUnused = Product("PRD-SAV-012M", "nội dung sản phẩm không dùng", 0.5);

    private static readonly KnowledgeMatch TemplateMatch = new(
        "kb:email-template:TPL-EMAIL-MATURITY-01", KnowledgeDocumentType.EmailTemplate, "nội dung template",
        new KnowledgeSourceMetadata("kb:email-template:TPL-EMAIL-MATURITY-01", KnowledgeDocumentType.EmailTemplate,
            null, "TPL-EMAIL-MATURITY-01", "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp-t"),
        0.5);

    /// <summary>A well-formed five-part body.</summary>
    private const string StructuredBody =
        "Kính gửi {{CUSTOMER_NAME}},\n\n"
        + "Cảm ơn Anh/Chị đã dành thời gian trao đổi về nhu cầu gửi tiết kiệm.\n\n"
        + "Sản phẩm tiền gửi kỳ hạn sáu tháng phù hợp với khách hàng ưu tiên an toàn.\n\n"
        + "Anh/Chị vui lòng phản hồi khoảng thời gian thuận tiện để chúng tôi trao đổi chi tiết hơn.\n\n"
        + "Trân trọng,";

    private static (EmailTools Tools, RoutingKnowledgeRetriever Knowledge, FakeEmailDraftGenerator Generator) CreateTools()
    {
        var crm = new FakeCrmGateway
        {
            FindCustomerResult = CustomerLookupResult.Found(Customer),
            InteractionsResult = [new InteractionDto("INT-0001", Customer.Id, "Call", DateTime.UtcNow, "summary", "outcome", null, true)],
        };
        var knowledge = new RoutingKnowledgeRetriever
        {
            ProductResult = KnowledgeSearchResult.Found([ProductUsed]),
            TemplateResult = KnowledgeSearchResult.Found([TemplateMatch]),
        };
        var generator = new FakeEmailDraftGenerator();
        var tools = new EmailTools(crm, knowledge, generator, new HttpContextAccessor(), NullLogger<EmailTools>.Instance);
        return (tools, knowledge, generator);
    }

    private static RawEmailDraftModel Raw(string body, IEnumerable<string>? usedSourceIds = null, string? subject = null) => new(
        RawEmailDraftModel.StatusOk,
        subject ?? "Thông tin tham khảo về tiền gửi kỳ hạn 6 tháng",
        body,
        "PRD-SAV-006M",
        (usedSourceIds ?? ["kb:product:PRD-SAV-006M"]).ToArray(),
        true,
        []);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static async Task<JsonElement> GenerateAsync(EmailTools tools) =>
        Parse(await tools.GenerateEmail("CUS-0001", "Follow-up", cancellationToken: TestContext.Current.CancellationToken));

    // ---- B: body structure ---------------------------------------------------------------------

    [Fact]
    public async Task StructuredBody_IsAcceptedOnTheFirstAttemptAndPreservesBlankLines()
    {
        var (tools, _, generator) = CreateTools();
        generator.Results.Enqueue(Raw(StructuredBody));

        var root = await GenerateAsync(tools);

        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(1, generator.CallCount);

        var body = root.GetProperty("data").GetProperty("draft").GetProperty("body").GetString()!;
        // Blank-line separators survive into the RM-facing draft — the UI renders them because
        // .draft-body uses white-space: pre-wrap.
        Assert.Contains("\n\n", body, StringComparison.Ordinal);
        Assert.True(
            body.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Length >= 4,
            "The draft must reach the RM as several paragraphs.");
        // Greeting and closing are present and in the right places.
        Assert.StartsWith("Kính gửi ", body, StringComparison.Ordinal);
        Assert.EndsWith("Trân trọng,", body, StringComparison.Ordinal);
        Assert.Contains(Customer.FullName, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleParagraphBody_IsRejectedThenSucceedsOnTheRetry()
    {
        var (tools, _, generator) = CreateTools();
        generator.Results.Enqueue(Raw("Kính gửi {{CUSTOMER_NAME}}, đây là toàn bộ nội dung trong một khối duy nhất."));
        generator.Results.Enqueue(Raw(StructuredBody));

        var root = await GenerateAsync(tools);

        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(2, generator.CallCount);
        Assert.Contains("MỘT DÒNG TRỐNG", generator.LastContext!.CorrectiveInstruction!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleParagraphBodyTwice_ReturnsModelErrorWithoutAThirdAttempt()
    {
        var (tools, _, generator) = CreateTools();
        generator.Results.Enqueue(Raw("Một khối duy nhất, không có đoạn nào."));
        generator.Results.Enqueue(Raw("Vẫn là một khối duy nhất."));

        var root = await GenerateAsync(tools);

        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, generator.CallCount);
    }

    [Theory]
    [InlineData("<p>Kính gửi {{CUSTOMER_NAME}},</p>\n\n<p>Nội dung.</p>\n\n<p>Trân trọng,</p>")]
    [InlineData("Kính gửi {{CUSTOMER_NAME}},\n\nNội dung <br> xuống dòng.\n\nTrân trọng,")]
    [InlineData("Kính gửi {{CUSTOMER_NAME}},\n\n**Nội dung in đậm**.\n\nTrân trọng,")]
    [InlineData("Kính gửi {{CUSTOMER_NAME}},\n\n## Tiêu đề\n\nTrân trọng,")]
    public async Task MarkupInBody_IsRejected(string markupBody)
    {
        var (tools, _, generator) = CreateTools();
        generator.Results.Enqueue(Raw(markupBody));
        generator.Results.Enqueue(Raw(StructuredBody));

        var root = await GenerateAsync(tools);

        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(2, generator.CallCount);
        Assert.Contains("Markdown", generator.LastContext!.CorrectiveInstruction!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkupInSubject_IsAlsoRejected()
    {
        var (tools, _, generator) = CreateTools();
        generator.Results.Enqueue(Raw(StructuredBody, subject: "**Thông tin tham khảo**"));
        generator.Results.Enqueue(Raw(StructuredBody));

        var root = await GenerateAsync(tools);

        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(2, generator.CallCount);
    }

    /// <summary>
    /// A dash-prefixed line is ordinary Vietnamese business prose, not Markdown. Flagging it would
    /// send perfectly good drafts back for a pointless retry, so the markup check deliberately
    /// ignores it.
    /// </summary>
    [Fact]
    public async Task DashPrefixedLine_IsNotTreatedAsMarkdown()
    {
        var (tools, _, generator) = CreateTools();
        generator.Results.Enqueue(Raw(
            "Kính gửi {{CUSTOMER_NAME}},\n\nMột số điểm chính:\n- Kỳ hạn sáu tháng\n- Quản lý trên kênh số\n\n"
            + "Mong Anh/Chị phản hồi.\n\nTrân trọng,"));

        var root = await GenerateAsync(tools);

        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(1, generator.CallCount);
    }

    // ---- C: sources ----------------------------------------------------------------------------

    /// <summary>
    /// generate_email retrieves with an explicit [Product] / [EmailTemplate] filter, so a
    /// call-script playbook can never enter its evidence set even though all three document types
    /// share one Chroma collection. Asserted rather than assumed: the two tools were added together.
    /// </summary>
    [Fact]
    public async Task SourceIds_NeverContainACallScript_EvenIfTheModelTriesToCiteOne()
    {
        var (tools, knowledge, generator) = CreateTools();
        generator.Results.Enqueue(Raw(
            StructuredBody,
            usedSourceIds: ["kb:product:PRD-SAV-006M", "kb:call-script:CS-CALL-SAVINGS-FOLLOWUP-01"]));
        generator.Results.Enqueue(Raw(StructuredBody));

        var root = await GenerateAsync(tools);

        // The invented call-script citation is outside the allowed evidence set, so the first
        // attempt is rejected and the retry produces a clean draft.
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());

        var sourceIds = root.GetProperty("data").GetProperty("draft").GetProperty("sourceIds")
            .EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.DoesNotContain(sourceIds, id => id.StartsWith("kb:call-script:", StringComparison.Ordinal));

        // The retriever was only ever asked for products and email templates.
        Assert.Equal([KnowledgeDocumentType.Product], knowledge.LastProductQuery!.DocumentTypes);
        Assert.Equal([KnowledgeDocumentType.EmailTemplate], knowledge.LastTemplateQuery!.DocumentTypes);
    }

    [Fact]
    public async Task SourceIds_ExcludeRetrievedProductsTheDraftDidNotUse()
    {
        var (tools, knowledge, generator) = CreateTools();
        knowledge.ProductResult = KnowledgeSearchResult.Found([ProductUsed, ProductUnused]);
        generator.Results.Enqueue(Raw(StructuredBody, usedSourceIds: ["kb:product:PRD-SAV-006M"]));

        var root = await GenerateAsync(tools);

        var sourceIds = root.GetProperty("data").GetProperty("draft").GetProperty("sourceIds")
            .EnumerateArray().Select(e => e.GetString()!).ToList();

        Assert.Contains("kb:product:PRD-SAV-006M", sourceIds);
        Assert.DoesNotContain("kb:product:PRD-SAV-012M", sourceIds);
        Assert.DoesNotContain("kb:email-template:TPL-EMAIL-MATURITY-01", sourceIds);
    }

    [Fact]
    public async Task SourceIds_AtToolLevelMatchTheEnvelopeSourceIds()
    {
        var (tools, _, generator) = CreateTools();
        generator.Results.Enqueue(Raw(
            StructuredBody, usedSourceIds: ["kb:product:PRD-SAV-006M", "kb:email-template:TPL-EMAIL-MATURITY-01"]));

        var root = await GenerateAsync(tools);

        var envelope = root.GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()).ToList();
        var draft = root.GetProperty("data").GetProperty("draft").GetProperty("sourceIds")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(draft, envelope);
    }
}
