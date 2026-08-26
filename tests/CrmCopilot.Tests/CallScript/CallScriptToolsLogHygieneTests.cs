using System.Text;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.CallScript;
using CrmCopilot.Tests.CallScript.TestSupport;
using CrmCopilot.Tests.Crm.TestSupport;
using CrmCopilot.Tests.Email.TestSupport;
using Microsoft.AspNetCore.Http;

namespace CrmCopilot.Tests.CallScript;

/// <summary>
/// Log-hygiene sweep for generate_call_script, mirroring EmailToolsLogHygieneTests: drives the tool
/// through several outcome branches with a CapturingLogger and asserts the ENTIRE captured log text
/// (rendered messages + structured state, across every call) never contains an API-key-shaped
/// string, raw customer PII, raw model output, a caught SDK exception's message, or the exact
/// opportunity amount — and that no Exception object is ever handed to ILogger at all.
/// </summary>
public class CallScriptToolsLogHygieneTests
{
    private const string ApiKeyMarker = "AIzaSyDaGmWKa4JsXZHjGw7ISLn3namBGewQe";
    private static readonly string SdkExceptionMarker = $"upstream error body: ...?key={ApiKeyMarker}...";
    private const string ModelOutputMarker = "một đoạn kịch bản rất cụ thể do model sinh ra";

    /// <summary>CUS-0001's real opportunity amount in the checked-in dataset (OPP-0002).</summary>
    private const string ExactOpportunityAmount = "250000000";

    private static readonly CustomerDto Customer = new(
        "CUS-0001", "Nguyễn Minh Anh", "minh.anh@example.test", "0900000001", "000000000001",
        "Priority", "Hà Nội", "vi", "RM-0001", "Active", true, DateTime.UtcNow);

    private static readonly KnowledgeSourceMetadata ProductMetadata = new(
        "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp");

    private static readonly KnowledgeMatch ProductMatch =
        new("kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "nội dung sản phẩm", ProductMetadata, 0.4);

    private static readonly CallScriptEvidence ScriptEvidence =
        new("kb:call-script:CS-CALL-SAVINGS-FOLLOWUP-01", "CS-CALL-SAVINGS-FOLLOWUP-01", "hướng dẫn playbook");

    private static OpportunityDto SampleOpportunity() =>
        new("OPP-0002", Customer.Id, "PRD-SAV-006M", "Proposal", 250_000_000,
            new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), OpportunityStatuses.Open, true);

    private static InteractionDto SampleInteraction() =>
        new("INT-0001", Customer.Id, "Call", DateTime.UtcNow, "summary", "outcome", null, true);

    private static (CallScriptTools Tools, FakeCrmGateway Crm, CallScriptRoutingKnowledgeRetriever Knowledge,
        FakeCallScriptGenerator Generator, CapturingLogger<CallScriptTools> Logger) CreateTools()
    {
        var crm = new FakeCrmGateway
        {
            FindCustomerResult = CustomerLookupResult.Found(Customer),
            InteractionsResult = [SampleInteraction()],
            OpportunitiesResult = [SampleOpportunity()],
        };
        var knowledge = new CallScriptRoutingKnowledgeRetriever
        {
            ProductResult = KnowledgeSearchResult.Found([ProductMatch]),
            CallScriptResult = KnowledgeSearchResult.Found(
                [new KnowledgeMatch(ScriptEvidence.SourceId, KnowledgeDocumentType.CallScript, ScriptEvidence.Content,
                    new KnowledgeSourceMetadata(ScriptEvidence.SourceId, KnowledgeDocumentType.CallScript, null,
                        ScriptEvidence.ScriptId, "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp"), 0.5)]),
        };
        var generator = new FakeCallScriptGenerator();
        var logger = new CapturingLogger<CallScriptTools>();
        var tools = new CallScriptTools(
            crm, knowledge, generator, new FakeCallScriptTemplateCatalog(), new HttpContextAccessor(), logger);
        return (tools, crm, knowledge, generator, logger);
    }

    private static RawCallScriptModel ValidRaw(string opening, IReadOnlyList<string>? warnings = null) => new(
        RawCallScriptModel.StatusOk,
        opening,
        ["Anh hoặc Chị dự định gửi trong bao lâu?"],
        ["Sản phẩm phù hợp với nhu cầu an toàn."],
        [new RawObjectionHandlingItem("Tôi cần cân nhắc thêm.", "Vâng, tôi sẽ liên hệ lại sau.")],
        "Cảm ơn Anh hoặc Chị đã dành thời gian.",
        "PRD-SAV-006M",
        ["kb:product:PRD-SAV-006M"],
        true,
        warnings ?? []);

    [Fact]
    public async Task GenerateCallScript_AcrossAllOutcomes_LogsNeverContainRawPiiApiKeyModelOutputAmountOrSdkExceptionText()
    {
        var combined = new StringBuilder();
        var allEntries = new List<LogEntry>();

        async Task RunAsync(Action<FakeCrmGateway, CallScriptRoutingKnowledgeRetriever, FakeCallScriptGenerator> arrange)
        {
            var (tools, crm, knowledge, generator, logger) = CreateTools();
            arrange(crm, knowledge, generator);
            await tools.GenerateCallScript("CUS-0001", "mục tiêu", cancellationToken: TestContext.Current.CancellationToken);
            combined.AppendLine(logger.AllCapturedText());
            allEntries.AddRange(logger.Entries);
        }

        // Success, with a marker embedded in the model's own opening text.
        await RunAsync((_, _, generator) =>
            generator.Results.Enqueue(ValidRaw($"Kính chào {{{{CUSTOMER_NAME}}}}, {ModelOutputMarker}")));

        // Model-authored warnings must be counted, never echoed.
        await RunAsync((_, _, generator) =>
            generator.Results.Enqueue(ValidRaw("Kính chào {{CUSTOMER_NAME}}, nội dung.", warnings: [ModelOutputMarker])));

        // CRM upstream failure carrying a verbose body with an API-key-shaped substring.
        await RunAsync((crm, _, _) =>
            crm.ThrowOnFindCustomer = new CrmUpstreamException(SdkExceptionMarker, retryable: true, traceId: null));

        // Opportunity lookup failure — a P0-10-specific path EmailTools has no equivalent of.
        await RunAsync((crm, _, _) =>
            crm.ThrowOnGetOpportunities = new CrmUpstreamException(SdkExceptionMarker, retryable: true, traceId: null));

        // Knowledge/embedding failure.
        await RunAsync((_, knowledge, _) =>
            knowledge.ThrowOnCallScriptSearch = new KnowledgeEmbeddingException(SdkExceptionMarker, retryable: true));

        // Generation failure wrapping an SDK-shaped inner exception carrying the marker.
        await RunAsync((_, _, generator) =>
            generator.ThrowOnGenerate = new CallScriptGenerationException(retryable: true, new InvalidOperationException(SdkExceptionMarker)));

        // Unexpected exception (catch-all row).
        await RunAsync((crm, _, _) => crm.ThrowOnFindCustomer = new InvalidOperationException(SdkExceptionMarker));

        var text = combined.ToString();

        Assert.DoesNotContain(ApiKeyMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SdkExceptionMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ModelOutputMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Customer.FullName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Customer.Email, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Customer.Phone, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Customer.AccountReference, text, StringComparison.Ordinal);
        // P0-10 specific: the audit log records the selected opportunity id, never its value.
        Assert.DoesNotContain(ExactOpportunityAmount, text, StringComparison.Ordinal);
        // Proves the production rule directly: no ILogger overload taking an Exception is ever used.
        Assert.All(allEntries, entry => Assert.Null(entry.Exception));
    }

    /// <summary>
    /// The audit line must still be useful: hashed customer id, the selected opportunity id, the
    /// objective provenance and the source ids are all safe and all needed to trace a run.
    /// </summary>
    [Fact]
    public async Task GenerateCallScript_AuditLog_CarriesDerivedSafeFields()
    {
        var (tools, _, _, generator, logger) = CreateTools();
        generator.Results.Enqueue(ValidRaw("Kính chào {{CUSTOMER_NAME}}, nội dung."));

        await tools.GenerateCallScript("CUS-0001", "mục tiêu", cancellationToken: TestContext.Current.CancellationToken);

        var text = logger.AllCapturedText();
        Assert.Contains("generate_call_script", text, StringComparison.Ordinal);
        Assert.Contains("status=success", text, StringComparison.Ordinal);
        Assert.Contains("selectedOpportunityId=OPP-0002", text, StringComparison.Ordinal);
        Assert.Contains($"objectiveSource={CallScriptObjectiveSources.Request}", text, StringComparison.Ordinal);
        Assert.Contains("customerIdHash=", text, StringComparison.Ordinal);
        // The raw customer id is replaced by its hash in the audit field.
        Assert.DoesNotContain("customerIdHash=CUS-0001", text, StringComparison.Ordinal);
    }
}
