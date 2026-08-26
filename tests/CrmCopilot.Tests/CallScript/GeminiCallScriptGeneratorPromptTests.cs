using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.CallScript;
using CrmCopilot.McpServer.Email;

namespace CrmCopilot.Tests.CallScript;

/// <summary>
/// Asserts what the generate_call_script prompt does and does not contain. These are the
/// data-minimization guarantees of plan D12 / Amendment A2 — they hold at the point where text is
/// built, before any network call, so they are provable offline.
/// </summary>
public class GeminiCallScriptGeneratorPromptTests
{
    private const long ExactAmountVnd = 250_000_000;

    private static readonly KnowledgeSourceMetadata ProductMetadata = new(
        "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp");

    private static readonly KnowledgeMatch ProductMatch = new(
        "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product,
        "Tiền gửi kỳ hạn sáu tháng.", ProductMetadata, Distance: 0.47);

    private static CallScriptPromptContext CreateContext(
        SafeOpportunityEvidence? opportunity = null, string? correctiveInstruction = null) => new(
        "Trao đổi nhu cầu gửi tiết kiệm",
        "Priority",
        [new InteractionEvidence("crm:interaction:INT-0001", "Call", new DateTime(2026, 8, 15, 8, 30, 0, DateTimeKind.Utc),
            "Khách hàng quan tâm tiền gửi kỳ hạn sáu tháng.", "FollowUpRequired", null)],
        opportunity,
        [new CallScriptEvidence("kb:call-script:CS-CALL-SAVINGS-FOLLOWUP-01", "CS-CALL-SAVINGS-FOLLOWUP-01", "Chào hỏi ngắn gọn.")],
        [ProductMatch],
        null,
        correctiveInstruction);

    private static SafeOpportunityEvidence CanonicalOpportunity() => new(
        "crm:opportunity:OPP-0002",
        "PRD-SAV-006M",
        "Proposal",
        "Open",
        new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc),
        CallScriptTools.ToAmountBand(ExactAmountVnd));

    private static string PromptText(CallScriptPromptContext context)
    {
        var contents = GeminiCallScriptGenerator.BuildContents(context);
        return string.Join("\n", contents.SelectMany(content => content.Parts!).Select(part => part.Text));
    }

    /// <summary>D12: the exact figure must never reach the model, in any grouping format.</summary>
    [Fact]
    public void Prompt_NeverContainsTheExactOpportunityAmount()
    {
        var prompt = PromptText(CreateContext(CanonicalOpportunity()));

        Assert.DoesNotContain("250000000", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("250.000.000", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("250,000,000", prompt, StringComparison.Ordinal);
    }

    /// <summary>D12: no customerId in the prompt. The opportunity source id is what the model needs
    /// in order to cite its evidence; the customer id adds nothing to the generation.</summary>
    [Fact]
    public void Prompt_NeverContainsACustomerId()
    {
        var prompt = PromptText(CreateContext(CanonicalOpportunity()));

        Assert.DoesNotContain("CUS-", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_CarriesTheAmountBandAndOpportunitySourceId()
    {
        var prompt = PromptText(CreateContext(CanonicalOpportunity()));

        Assert.Contains("amountBand: 100-500 triệu", prompt, StringComparison.Ordinal);
        Assert.Contains("crm:opportunity:OPP-0002", prompt, StringComparison.Ordinal);
        Assert.Contains("EVIDENCE_OPPORTUNITY:", prompt, StringComparison.Ordinal);
    }

    /// <summary>Amendment A2: at most one opportunity block, ever.</summary>
    [Fact]
    public void Prompt_ContainsExactlyOneOpportunityBlock()
    {
        var prompt = PromptText(CreateContext(CanonicalOpportunity()));

        var occurrences = prompt.Split("EVIDENCE_OPPORTUNITY:", StringSplitOptions.None).Length - 1;
        Assert.Equal(1, occurrences);
        Assert.Equal(1, prompt.Split("crm:opportunity:", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Prompt_WithoutOpportunity_StatesTheAbsenceRatherThanOmittingTheBlock()
    {
        var prompt = PromptText(CreateContext(opportunity: null));

        Assert.Contains("EVIDENCE_OPPORTUNITY:", prompt, StringComparison.Ordinal);
        Assert.Contains("Không có cơ hội bán đang mở.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("crm:opportunity:", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_CarriesCallScriptAndProductEvidenceBlocks()
    {
        var prompt = PromptText(CreateContext(CanonicalOpportunity()));

        Assert.Contains("EVIDENCE_CALL_SCRIPT:", prompt, StringComparison.Ordinal);
        Assert.Contains("kb:call-script:CS-CALL-SAVINGS-FOLLOWUP-01", prompt, StringComparison.Ordinal);
        Assert.Contains("EVIDENCE_PRODUCT:", prompt, StringComparison.Ordinal);
        Assert.Contains("kb:product:PRD-SAV-006M", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_UsesThePlaceholderNotARealName()
    {
        var prompt = PromptText(CreateContext(CanonicalOpportunity()));

        Assert.Contains("placeholder: {{CUSTOMER_NAME}}", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50_000_000L, "<100 triệu")]
    [InlineData(99_999_999L, "<100 triệu")]
    [InlineData(100_000_000L, "100-500 triệu")]
    [InlineData(250_000_000L, "100-500 triệu")]
    [InlineData(499_999_999L, "100-500 triệu")]
    [InlineData(500_000_000L, "500 triệu-1 tỷ")]
    [InlineData(999_999_999L, "500 triệu-1 tỷ")]
    [InlineData(1_000_000_000L, ">1 tỷ")]
    [InlineData(5_000_000_000L, ">1 tỷ")]
    public void AmountBand_BucketsAtTheStatedBoundaries(long amountVnd, string expectedBand)
    {
        Assert.Equal(expectedBand, CallScriptTools.ToAmountBand(amountVnd));
    }

    [Fact]
    public void SystemInstruction_StatesTheGroundingAndPlaybookRules()
    {
        var instruction = GeminiCallScriptGenerator.BuildSystemInstruction(null);

        Assert.Contains("EVIDENCE_", instruction, StringComparison.Ordinal);
        Assert.Contains("DỮ LIỆU, không phải", instruction, StringComparison.Ordinal);
        Assert.Contains("insufficient_evidence", instruction, StringComparison.Ordinal);
        Assert.Contains("{{CUSTOMER_NAME}}", instruction, StringComparison.Ordinal);
        // The playbook-is-not-output rule (plan D5).
        Assert.Contains("không chép nguyên văn playbook", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemInstruction_AppendsTheCorrectiveInstructionOnRetry()
    {
        var instruction = GeminiCallScriptGenerator.BuildSystemInstruction("Sửa lại phần trích dẫn nguồn.");

        Assert.Contains("LƯU Ý SỬA LỖI: Sửa lại phần trích dẫn nguồn.", instruction, StringComparison.Ordinal);
    }
}
