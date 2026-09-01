using System.Text;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.McpServer.Email;
using CrmCopilot.Tests.Crm.TestSupport;
using CrmCopilot.Tests.Email.TestSupport;
using Microsoft.AspNetCore.Http;

namespace CrmCopilot.Tests.Email;

/// <summary>
/// Full-coverage log-hygiene sweep (P0-07 amendment ✏️26/A3): drives EmailTools through several
/// outcome branches with a CapturingLogger and asserts the ENTIRE captured log text (rendered
/// messages + structured state, across every call) never contains an API-key-shaped string, raw
/// customer PII, raw model output text, or a caught SDK exception's raw message — proving
/// EmailTools never passes an Exception object itself into ILogger (only derived-safe values).
/// </summary>
public class EmailToolsLogHygieneTests
{
    private static readonly CustomerDto Customer = new(
        "CUS-0001", "Nguyễn Minh Anh", "minh.anh@example.test", "0900000001", "000000000001",
        "Priority", "Hà Nội", "vi", "RM-0001", "Active", true, DateTime.UtcNow);

    private const string ApiKeyMarker = "AIzaSyDaGmWKa4JsXZHjGw7ISLn3namBGewQe";
    private static readonly string SdkExceptionMarker = $"upstream error body: ...?key={ApiKeyMarker}...";
    private const string ModelOutputMarker = "một đoạn văn bản rất cụ thể do model sinh ra";

    private static readonly KnowledgeSourceMetadata ProductMetadata = new(
        "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp");

    private static readonly KnowledgeMatch ProductMatch =
        new("kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "nội dung", ProductMetadata, 0.4);

    private static InteractionDto SampleInteraction() =>
        new("INT-0001", Customer.Id, "Call", DateTime.UtcNow, "summary", "outcome", null, true);

    private static (EmailTools Tools, FakeCrmGateway Crm, RoutingKnowledgeRetriever Knowledge, FakeEmailDraftGenerator Generator, CapturingLogger<EmailTools> Logger)
        CreateTools()
    {
        var crm = new FakeCrmGateway { FindCustomerResult = CustomerLookupResult.Found(Customer), InteractionsResult = [SampleInteraction()] };
        var knowledge = new RoutingKnowledgeRetriever
        {
            ProductResult = KnowledgeSearchResult.Found([ProductMatch]),
            TemplateResult = KnowledgeSearchResult.NoRelevantEvidence,
        };
        var generator = new FakeEmailDraftGenerator();
        var logger = new CapturingLogger<EmailTools>();
        var tools = new EmailTools(crm, knowledge, generator, new HttpContextAccessor(), logger);
        return (tools, crm, knowledge, generator, logger);
    }

    private static RawEmailDraftModel ValidRaw(string body, IReadOnlyList<string>? warnings = null) => new(
        RawEmailDraftModel.StatusOk, "Thông tin sản phẩm", body, "PRD-SAV-006M", ["kb:product:PRD-SAV-006M"], true, warnings ?? []);

    [Fact]
    public async Task GenerateEmail_AcrossAllOutcomes_LogsNeverContainRawPiiApiKeyModelOutputOrSdkExceptionText()
    {
        var combined = new StringBuilder();
        var allEntries = new List<LogEntry>();

        // Success, with a marker embedded in the model's body text.
        {
            var (tools, _, _, generator, logger) = CreateTools();
            generator.Results.Enqueue(ValidRaw($"Kính gửi {{{{CUSTOMER_NAME}}}}, {ModelOutputMarker}"));
            await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);
            combined.AppendLine(logger.AllCapturedText());
            allEntries.AddRange(logger.Entries);
        }

        // CrmUpstreamException carrying a verbose upstream body (simulated) with an API-key-shaped substring.
        {
            var (tools, crm, _, _, logger) = CreateTools();
            crm.ThrowOnFindCustomer = new CrmUpstreamException(SdkExceptionMarker, retryable: true, traceId: null);
            await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);
            combined.AppendLine(logger.AllCapturedText());
            allEntries.AddRange(logger.Entries);
        }

        // KnowledgeEmbeddingException, same marker.
        {
            var (tools, _, knowledge, _, logger) = CreateTools();
            knowledge.ThrowOnProductSearch = new KnowledgeEmbeddingException(SdkExceptionMarker, retryable: true);
            await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);
            combined.AppendLine(logger.AllCapturedText());
            allEntries.AddRange(logger.Entries);
        }

        // EmailGenerationException wrapping an SDK-shaped inner exception carrying the marker.
        {
            var (tools, _, _, generator, logger) = CreateTools();
            generator.ThrowOnGenerate = new EmailGenerationException(retryable: true, new InvalidOperationException(SdkExceptionMarker));
            await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);
            combined.AppendLine(logger.AllCapturedText());
            allEntries.AddRange(logger.Entries);
        }

        // Unexpected exception (catch-all row).
        {
            var (tools, crm, _, _, logger) = CreateTools();
            crm.ThrowOnFindCustomer = new InvalidOperationException(SdkExceptionMarker);
            await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);
            combined.AppendLine(logger.AllCapturedText());
            allEntries.AddRange(logger.Entries);
        }

        // Model warnings present.
        {
            var (tools, _, _, generator, logger) = CreateTools();
            generator.Results.Enqueue(ValidRaw("Kính gửi {{CUSTOMER_NAME}}, nội dung.", warnings: [ModelOutputMarker]));
            await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);
            combined.AppendLine(logger.AllCapturedText());
            allEntries.AddRange(logger.Entries);
        }

        var text = combined.ToString();

        Assert.DoesNotContain(ApiKeyMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SdkExceptionMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ModelOutputMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Customer.Email, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Customer.Phone, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Customer.AccountReference, text, StringComparison.Ordinal);
        // F2: Customer.FullName was not previously scanned for — the placeholder-restore path
        // legitimately reintroduces it into the *response*, but it must never appear in the audit
        // log (which only ever logs maskedFieldTypes, never draft subject/body content).
        Assert.DoesNotContain(Customer.FullName, text, StringComparison.Ordinal);
        // F2: proves the production rule directly — EmailTools never calls an ILogger overload
        // that accepts an Exception. If it ever did, this fails even if the exception's own
        // ToString() happened not to contain any of the scanned markers above.
        Assert.All(allEntries, entry => Assert.Null(entry.Exception));
    }
}
