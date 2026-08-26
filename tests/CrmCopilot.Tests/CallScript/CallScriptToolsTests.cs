using System.Text.Json;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.CallScript;
using CrmCopilot.Tests.Acceptance.TestSupport;
using CrmCopilot.Tests.CallScript.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.CallScript;

/// <summary>
/// Branch coverage for generate_call_script (plan §3 + Amendments A2/A3/A5/A6/A7).
///
/// Backed by DatasetCrmGateway — the real checked-in dataset and the real CrmDataset queries — so
/// opportunity selection, the customer with no Open opportunity, and the wrong-customer
/// opportunity case all come from production data rather than from stub values a test could bend
/// to agree with itself.
/// </summary>
public class CallScriptToolsTests
{
    private const string CanonicalOpenOpportunityId = "OPP-0002";
    private const string OtherCustomerOpportunityId = "OPP-0003";
    private const string NoOpportunityCustomerId = "CUS-0004";
    private const string CallScriptSourceId = "kb:call-script:CS-CALL-SAVINGS-FOLLOWUP-01";
    private const string PinnedSourceId = "kb:call-script:CS-CALL-PERIODIC-CARE-01";

    private static readonly KnowledgeSourceMetadata ProductMetadata = new(
        ScenarioDatasetSeed.CanonicalProductSourceId, KnowledgeDocumentType.Product,
        ScenarioDatasetSeed.CanonicalProductCode, null,
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp-product");

    private static readonly KnowledgeMatch ProductMatch = new(
        ScenarioDatasetSeed.CanonicalProductSourceId, KnowledgeDocumentType.Product,
        "Tiền gửi kỳ hạn sáu tháng dành cho khách hàng ưu tiên an toàn.",
        ProductMetadata, Distance: 0.47);

    private static readonly KnowledgeSourceMetadata CallScriptMetadata = new(
        CallScriptSourceId, KnowledgeDocumentType.CallScript, null, "CS-CALL-SAVINGS-FOLLOWUP-01",
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp-call-script");

    private static readonly KnowledgeMatch CallScriptMatch = new(
        CallScriptSourceId, KnowledgeDocumentType.CallScript,
        "Chào hỏi ngắn gọn rồi nhắc lại nhu cầu gửi tiết kiệm đã trao đổi.",
        CallScriptMetadata, Distance: 0.51);

    // ---- retrieval candidates that must never be reported as sources unless the draft used them.
    // These are the exact ids the browser run wrongly attached to a savings call script.
    private const string UnrelatedCallScriptSourceId = "kb:call-script:CS-CALL-LOAN-REMINDER-01";

    private static KnowledgeMatch ProductCandidate(string code, string content, double distance) => new(
        $"kb:product:{code}", KnowledgeDocumentType.Product, content,
        new KnowledgeSourceMetadata($"kb:product:{code}", KnowledgeDocumentType.Product, code, null,
            "vi", "1.0", "gemini-embedding-001", 768, "l2", true, $"fp-{code}"),
        distance);

    private static readonly KnowledgeMatch UnrelatedSavingsMatch =
        ProductCandidate("PRD-SAV-012M", "Tiền gửi kỳ hạn mười hai tháng.", 0.63);

    private static readonly KnowledgeMatch UnrelatedLoanMatch =
        ProductCandidate("PRD-LOAN-PERSONAL-01", "Vay tiêu dùng cá nhân.", 0.71);

    private static readonly KnowledgeMatch UnrelatedCallScriptMatch = new(
        UnrelatedCallScriptSourceId, KnowledgeDocumentType.CallScript,
        "Nhắc lịch thanh toán khoản vay sắp đến hạn.",
        new KnowledgeSourceMetadata(UnrelatedCallScriptSourceId, KnowledgeDocumentType.CallScript, null,
            "CS-CALL-LOAN-REMINDER-01", "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp-loan-script"),
        Distance: 0.68);

    private sealed record Harness(
        CallScriptTools Tools,
        FakeCallScriptGenerator Generator,
        CallScriptRoutingKnowledgeRetriever Retriever,
        FakeCallScriptTemplateCatalog Catalog);

    private static Harness CreateHarness(bool withEvidence = true, bool withPinnedTemplate = false)
    {
        var retriever = new CallScriptRoutingKnowledgeRetriever();
        if (withEvidence)
        {
            retriever.ProductResult = KnowledgeSearchResult.Found([ProductMatch]);
            retriever.CallScriptResult = KnowledgeSearchResult.Found([CallScriptMatch]);
        }

        var catalog = new FakeCallScriptTemplateCatalog();
        if (withPinnedTemplate)
        {
            catalog.Entries[CallScriptGenerationOptions.PeriodicCareScriptId] =
                new CallScriptEvidence(PinnedSourceId, CallScriptGenerationOptions.PeriodicCareScriptId,
                    "Chào hỏi thân thiện rồi hỏi thăm nhu cầu hiện tại của khách hàng.");
        }

        var generator = new FakeCallScriptGenerator();
        var tools = new CallScriptTools(
            new DatasetCrmGateway(), retriever, generator, catalog, new HttpContextAccessor(),
            NullLogger<CallScriptTools>.Instance);

        return new Harness(tools, generator, retriever, catalog);
    }

    /// <summary>Valid model output: fully accented Vietnamese, no digits, cites the product.</summary>
    private static RawCallScriptModel ValidRaw(
        IReadOnlyList<string>? usedSourceIds = null, string? suggestedProductCode = null) => new(
        RawCallScriptModel.StatusOk,
        "Kính chào {{CUSTOMER_NAME}}, tôi gọi để trao đổi về nhu cầu gửi tiết kiệm của Anh hoặc Chị.",
        ["Anh hoặc Chị dự định gửi trong khoảng thời gian nào?", "Điều gì khiến Anh hoặc Chị còn băn khoăn?"],
        ["Sản phẩm phù hợp với khách hàng ưu tiên an toàn.", "Có thể quản lý hoàn toàn trên kênh số."],
        [new RawObjectionHandlingItem("Tôi cần thêm thời gian cân nhắc.", "Hoàn toàn hợp lý, tôi sẽ liên hệ lại sau.")],
        "Cảm ơn Anh hoặc Chị đã dành thời gian trao đổi hôm nay.",
        suggestedProductCode ?? ScenarioDatasetSeed.CanonicalProductCode,
        usedSourceIds ?? [ScenarioDatasetSeed.CanonicalProductSourceId],
        RequiresHumanApproval: false, // deliberately false — the tool must override it
        []);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement Draft(string json) => Parse(json).GetProperty("data").GetProperty("draft");

    // ---- validation ------------------------------------------------------------------------

    [Fact]
    public async Task BlankCustomerId_ReturnsInvalidArgument()
    {
        var harness = CreateHarness();

        var result = await harness.Tools.GenerateCallScript("  ", null, null, null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, harness.Generator.CallCount);
    }

    [Fact]
    public async Task ObjectiveOverMaxLength_ReturnsInvalidArgument()
    {
        var harness = CreateHarness();
        var objective = new string('a', CallScriptGenerationOptions.MaxObjectiveLength + 1);

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, objective, null, null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal(0, harness.Generator.CallCount);
    }

    [Theory]
    [InlineData("khong-dung-format")]
    [InlineData("prd-sav-006m")]
    [InlineData("PRD")]
    public async Task MalformedProductCode_ReturnsInvalidArgument(string productCode)
    {
        var harness = CreateHarness();

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", null, productCode, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, harness.Generator.CallCount);
    }

    [Theory]
    [InlineData("OPP-2")]
    [InlineData("opp-0002")]
    [InlineData("OPP-00002")]
    public async Task MalformedOpportunityId_ReturnsInvalidArgument(string opportunityId)
    {
        var harness = CreateHarness();

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", opportunityId, null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, harness.Generator.CallCount);
    }

    // ---- not-found gates, none of which may reach Gemini (A7-4, A7-5, A7-9, D15) -------------

    /// <summary>A7-9: the canonical id is CUS-0001; the CS- typo must be a clean NOT_FOUND, not an
    /// INVALID_ARGUMENT, because customerId is deliberately not shape-validated.</summary>
    [Fact]
    public async Task CustomerIdTypo_ReturnsNotFoundNotInvalidArgument()
    {
        var harness = CreateHarness();

        var result = await harness.Tools.GenerateCallScript(
            "CS-0001", "Trao đổi nhu cầu", null, null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal(0, harness.Generator.CallCount);
    }

    /// <summary>A7-4.</summary>
    [Fact]
    public async Task OpportunityBelongingToAnotherCustomer_ReturnsNotFoundAndNeverCallsGemini()
    {
        var harness = CreateHarness();

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", OtherCustomerOpportunityId, null,
            TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal(0, harness.Generator.CallCount);
    }

    /// <summary>A7-5.</summary>
    [Fact]
    public async Task NonexistentOpportunityId_ReturnsNotFoundAndNeverCallsGemini()
    {
        var harness = CreateHarness();

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", "OPP-9999", null,
            TestContext.Current.CancellationToken);

        Assert.Equal(McpToolStatus.NotFound, Parse(result).GetProperty("status").GetString());
        Assert.Equal(0, harness.Generator.CallCount);
    }

    /// <summary>
    /// D15: a requested product with no matching product evidence is a hard stop. A call-script
    /// playbook explains how to talk, never what the product is, so it must not paper over this.
    /// </summary>
    [Fact]
    public async Task RequestedProductCodeWithoutMatchingEvidence_ReturnsRagNoEvidenceAndNeverCallsGemini()
    {
        var harness = CreateHarness();

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", null, "PRD-KHONG-TON-TAI-99",
            TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        // RagNoEvidence carries no error object — that is how a missing-evidence outcome is told
        // apart from a missing-entity one, structurally rather than by wording.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal(0, harness.Generator.CallCount);
    }

    [Fact]
    public async Task NoEvidenceAtAll_ReturnsRagNoEvidence()
    {
        var harness = CreateHarness(withEvidence: false);

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", null, null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(0, harness.Generator.CallCount);
    }

    [Fact]
    public async Task ModelReportsInsufficientEvidence_MapsToRagNoEvidenceAndIsNeverRetried()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(new RawCallScriptModel(
            RawCallScriptModel.StatusInsufficientEvidence, null, null, null, null, null, null, null, false, null));

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", null, null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(1, harness.Generator.CallCount); // terminal, not retried
    }

    // ---- success path ------------------------------------------------------------------------

    /// <summary>A7-6 and A7-7 plus the server-forced approval flag.</summary>
    [Fact]
    public async Task CanonicalCall_SelectsExactlyOneOpportunityAndCitesIt()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(ValidRaw());

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu gửi tiết kiệm", null, null,
            TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());

        var draft = Draft(result);
        Assert.Equal(CanonicalOpenOpportunityId, draft.GetProperty("selectedOpportunityId").GetString());
        Assert.True(draft.GetProperty("requiresHumanApproval").GetBoolean());

        // Exactly one opportunity reached the prompt.
        Assert.NotNull(harness.Generator.LastContext!.Opportunity);
        Assert.Equal($"crm:opportunity:{CanonicalOpenOpportunityId}", harness.Generator.LastContext.Opportunity!.SourceId);

        var sourceIds = draft.GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains($"crm:opportunity:{CanonicalOpenOpportunityId}", sourceIds);
        Assert.Equal(sourceIds.Count, sourceIds.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Browser-verified regression. A savings call script came back citing PRD-SAV-012M and
    /// PRD-LOAN-PERSONAL-01 — both merely retrieval candidates the draft never used — plus both
    /// retrieved playbooks. Sources are provenance an RM reads as "this is what the draft rests on",
    /// so listing candidates there overstates the grounding.
    ///
    /// The retrieval breadth is unchanged (three products, two playbooks reach the prompt); what
    /// changed is that only the ones the finished draft actually cited are reported.
    /// </summary>
    [Fact]
    public async Task SourceIds_ExcludeRetrievalCandidatesTheDraftDidNotUse()
    {
        var harness = CreateHarness();
        harness.Retriever.ProductResult = KnowledgeSearchResult.Found([ProductMatch, UnrelatedSavingsMatch, UnrelatedLoanMatch]);
        harness.Retriever.CallScriptResult = KnowledgeSearchResult.Found([CallScriptMatch, UnrelatedCallScriptMatch]);
        harness.Generator.Results.Enqueue(ValidRaw(
            usedSourceIds: [ScenarioDatasetSeed.CanonicalProductSourceId, CallScriptSourceId]));

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu gửi tiết kiệm kỳ hạn 6 tháng", null, null,
            TestContext.Current.CancellationToken);

        var sourceIds = Draft(result).GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()).ToList();

        // What the draft actually used.
        Assert.Contains(ScenarioDatasetSeed.CanonicalProductSourceId, sourceIds);
        Assert.Contains(CallScriptSourceId, sourceIds);

        // The exact ids the browser run wrongly reported.
        Assert.DoesNotContain("kb:product:PRD-SAV-012M", sourceIds);
        Assert.DoesNotContain("kb:product:PRD-LOAN-PERSONAL-01", sourceIds);

        // ...and the second playbook, which the draft never cited either.
        Assert.DoesNotContain(UnrelatedCallScriptSourceId, sourceIds);

        // All three products and both playbooks still reached the prompt — retrieval breadth is
        // deliberately untouched; only the reported provenance narrowed.
        Assert.Equal(3, harness.Generator.LastContext!.ProductMatches.Count);
        Assert.Equal(2, harness.Generator.LastContext.CallScriptMatches.Count);
    }

    /// <summary>
    /// The corroborated opportunity is still server-forced, so a source chip for it does not depend
    /// on the model volunteering the citation. OPP-0002 qualifies because it is an Open opportunity
    /// for PRD-SAV-006M — the very product this savings draft is about.
    /// </summary>
    [Fact]
    public async Task SourceIds_StillIncludeTheCorroboratedOpportunityWithoutTheModelCitingIt()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(ValidRaw(usedSourceIds: [ScenarioDatasetSeed.CanonicalProductSourceId]));

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu gửi tiết kiệm", null, null,
            TestContext.Current.CancellationToken);

        var draft = Draft(result);
        Assert.Contains(
            $"crm:opportunity:{CanonicalOpenOpportunityId}",
            draft.GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(CanonicalOpenOpportunityId, draft.GetProperty("selectedOpportunityId").GetString());
    }

    /// <summary>
    /// The other half of the same rule: an auto-selected opportunity the draft turns out not to be
    /// about is dropped from both the selection and the sources. Here the customer's only Open
    /// opportunity is for savings, but the draft is grounded in a loan product — so attaching the
    /// savings opportunity would misreport what the script rests on.
    /// </summary>
    [Fact]
    public async Task AutoSelectedOpportunityUnrelatedToTheDraft_IsDroppedFromSelectionAndSources()
    {
        var harness = CreateHarness();
        harness.Retriever.ProductResult = KnowledgeSearchResult.Found([UnrelatedLoanMatch]);
        harness.Generator.Results.Enqueue(ValidRaw(
            usedSourceIds: ["kb:product:PRD-LOAN-PERSONAL-01", CallScriptSourceId],
            suggestedProductCode: "PRD-LOAN-PERSONAL-01"));

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu vay tiêu dùng", null, null,
            TestContext.Current.CancellationToken);

        var draft = Draft(result);
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("selectedOpportunityId").ValueKind);
        Assert.DoesNotContain(
            $"crm:opportunity:{CanonicalOpenOpportunityId}",
            draft.GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task SuppliedObjective_IsUsedVerbatimAndCarriesNoInferredWarning()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(ValidRaw());

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu gửi tiết kiệm", null, null,
            TestContext.Current.CancellationToken);

        var draft = Draft(result);
        Assert.Equal("Trao đổi nhu cầu gửi tiết kiệm", draft.GetProperty("resolvedObjective").GetString());
        Assert.Equal(CallScriptObjectiveSources.Request, draft.GetProperty("objectiveSource").GetString());
        Assert.Empty(draft.GetProperty("warnings").EnumerateArray());
    }

    /// <summary>
    /// A7-8: the short-sentence demo. No objective supplied, so the tool derives one from the
    /// selected Open opportunity and says so through a warning code.
    /// </summary>
    [Fact]
    public async Task MissingObjective_IsInferredFromSelectedOpportunity()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(ValidRaw());

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, null, null, null, TestContext.Current.CancellationToken);

        var draft = Draft(result);
        Assert.Equal(CallScriptObjectiveSources.Opportunity, draft.GetProperty("objectiveSource").GetString());
        Assert.Equal(CanonicalOpenOpportunityId, draft.GetProperty("selectedOpportunityId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(draft.GetProperty("resolvedObjective").GetString()));

        var warnings = draft.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(CallScriptWarnings.ObjectiveInferred, warnings);
        Assert.DoesNotContain(CallScriptWarnings.TemplatePinned, warnings);
    }

    /// <summary>A7-10: no Open opportunity, so the periodic-care template is pinned by id.</summary>
    [Fact]
    public async Task MissingObjectiveAndNoOpenOpportunity_FallsBackToPinnedPeriodicCareTemplate()
    {
        var harness = CreateHarness(withPinnedTemplate: true);
        // The draft is built from the pinned playbook, so it cites it — sources now report what the
        // draft used rather than every candidate that reached the prompt.
        harness.Generator.Results.Enqueue(ValidRaw(
            usedSourceIds: [ScenarioDatasetSeed.CanonicalProductSourceId, PinnedSourceId]));

        var result = await harness.Tools.GenerateCallScript(
            NoOpportunityCustomerId, null, null, null, TestContext.Current.CancellationToken);

        var draft = Draft(result);
        Assert.Equal(CallScriptObjectiveSources.CustomerFollowUp, draft.GetProperty("objectiveSource").GetString());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("selectedOpportunityId").ValueKind);

        var warnings = draft.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(CallScriptWarnings.ObjectiveInferred, warnings);
        Assert.Contains(CallScriptWarnings.TemplatePinned, warnings);

        Assert.Contains(PinnedSourceId, draft.GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()));

        // The pin replaces semantic retrieval on this path — no call-script search was issued.
        Assert.Equal(0, harness.Retriever.CallScriptSearchCount);
    }

    /// <summary>
    /// When the pinned playbook is missing from the dataset the tool must fall back to real
    /// retrieval and must NOT claim TEMPLATE_PINNED — a warning has to describe what happened.
    /// </summary>
    [Fact]
    public async Task PinnedTemplateAbsent_FallsBackToRetrievalWithoutClaimingItWasPinned()
    {
        var harness = CreateHarness(withPinnedTemplate: false);
        harness.Generator.Results.Enqueue(ValidRaw());

        var result = await harness.Tools.GenerateCallScript(
            NoOpportunityCustomerId, null, null, null, TestContext.Current.CancellationToken);

        var draft = Draft(result);
        var warnings = draft.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(CallScriptWarnings.ObjectiveInferred, warnings);
        Assert.DoesNotContain(CallScriptWarnings.TemplatePinned, warnings);
        Assert.Equal(1, harness.Retriever.CallScriptSearchCount);
    }

    [Fact]
    public async Task ExplicitOpportunityId_OverridesTheDefaultOpenSelection()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(ValidRaw());

        // OPP-0001 is the canonical customer's Won opportunity — never the default pick.
        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", "OPP-0001", null,
            TestContext.Current.CancellationToken);

        Assert.Equal("OPP-0001", Draft(result).GetProperty("selectedOpportunityId").GetString());
    }

    [Fact]
    public async Task PlaceholderIsRestoredAndRawTokenNeverSurvives()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(ValidRaw());

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", null, null, TestContext.Current.CancellationToken);

        var opening = Draft(result).GetProperty("opening").GetString()!;
        Assert.DoesNotContain("{{CUSTOMER_NAME}}", opening, StringComparison.Ordinal);
        Assert.Contains(ScenarioDatasetSeed.CanonicalCustomer.FullName, opening, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelCitingSourceOutsideEvidence_IsRejectedThenRetriedOnce()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(ValidRaw(usedSourceIds: ["kb:product:PRD-FAKE-999"]));
        harness.Generator.Results.Enqueue(ValidRaw());

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", null, null, TestContext.Current.CancellationToken);

        Assert.Equal(McpToolStatus.Success, Parse(result).GetProperty("status").GetString());
        Assert.Equal(2, harness.Generator.CallCount);
        Assert.DoesNotContain(
            "kb:product:PRD-FAKE-999",
            Draft(result).GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task TwoInvalidAttempts_ReturnModelErrorAndNeverAThirdAttempt()
    {
        var harness = CreateHarness();
        harness.Generator.Results.Enqueue(ValidRaw(usedSourceIds: ["kb:product:PRD-FAKE-999"]));
        harness.Generator.Results.Enqueue(ValidRaw(usedSourceIds: ["kb:product:PRD-FAKE-999"]));

        var result = await harness.Tools.GenerateCallScript(
            ScenarioDatasetSeed.CanonicalCustomerId, "Trao đổi nhu cầu", null, null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(CallScriptGenerationOptions.MaxAttempts, harness.Generator.CallCount);
    }
}
