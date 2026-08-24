using System.Text.Json;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Email;
using CrmCopilot.Tests.Crm.TestSupport;
using CrmCopilot.Tests.Email.TestSupport;
using Microsoft.AspNetCore.Http;

namespace CrmCopilot.Tests.Mcp;

/// <summary>
/// Method-level coverage of every branch in the P0-07 plan's EmailTools error/outcome table
/// (§7.2) plus the amendment's productCode-format and raw-name-leak checks. Fast, offline, against
/// FakeCrmGateway/RoutingKnowledgeRetriever/FakeEmailDraftGenerator.
/// </summary>
public class EmailToolsTests
{
    private static readonly CustomerDto Customer = new(
        "CUS-0001", "Nguyễn Minh Anh", "minh.anh@example.test", "0900000001", "000000000001",
        "Priority", "Hà Nội", "vi", "RM-0001", "Active", true, DateTime.UtcNow);

    private static readonly KnowledgeSourceMetadata ProductMetadataA = new(
        "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp-a");

    private static readonly KnowledgeSourceMetadata ProductMetadataB = new(
        "kb:product:PRD-SAV-012M", KnowledgeDocumentType.Product, "PRD-SAV-012M", null,
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp-b");

    private static readonly KnowledgeSourceMetadata TemplateMetadata = new(
        "kb:email-template:TPL-EMAIL-MATURITY-01", KnowledgeDocumentType.EmailTemplate, null, "TPL-EMAIL-MATURITY-01",
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fp-t");

    private static readonly KnowledgeMatch ProductMatchA =
        new("kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "nội dung sản phẩm A", ProductMetadataA, 0.4);

    private static readonly KnowledgeMatch ProductMatchB =
        new("kb:product:PRD-SAV-012M", KnowledgeDocumentType.Product, "nội dung sản phẩm B", ProductMetadataB, 0.5);

    private static readonly KnowledgeMatch TemplateMatch =
        new("kb:email-template:TPL-EMAIL-MATURITY-01", KnowledgeDocumentType.EmailTemplate, "nội dung template", TemplateMetadata, 0.5);

    private static InteractionDto SampleInteraction() =>
        new("INT-0001", Customer.Id, "Call", DateTime.UtcNow, "summary", "outcome", null, true);

    private static RawEmailDraftModel ValidRaw(
        IEnumerable<string>? usedSourceIds = null,
        string? suggestedProductCode = "PRD-SAV-006M",
        string subject = "Thông tin tham khảo về tiền gửi kỳ hạn 6 tháng",
        string body = "Kính gửi {{CUSTOMER_NAME}}, đây là thông tin tham khảo.",
        bool requiresHumanApproval = true,
        IReadOnlyList<string>? warnings = null) =>
        new(
            RawEmailDraftModel.StatusOk,
            subject,
            body,
            suggestedProductCode,
            (usedSourceIds ?? ["kb:product:PRD-SAV-006M"]).ToArray(),
            requiresHumanApproval,
            warnings ?? []);

    private static RawEmailDraftModel InsufficientEvidenceRaw() =>
        new(RawEmailDraftModel.StatusInsufficientEvidence, "", "", null, [], true, []);

    private static (EmailTools Tools, FakeCrmGateway Crm, RoutingKnowledgeRetriever Knowledge, FakeEmailDraftGenerator Generator, CapturingLogger<EmailTools> Logger)
        CreateTools()
    {
        var crm = new FakeCrmGateway
        {
            FindCustomerResult = CustomerLookupResult.Found(Customer),
            InteractionsResult = [SampleInteraction()],
        };
        var knowledge = new RoutingKnowledgeRetriever
        {
            ProductResult = KnowledgeSearchResult.Found([ProductMatchA]),
            TemplateResult = KnowledgeSearchResult.Found([TemplateMatch]),
        };
        var generator = new FakeEmailDraftGenerator();
        var logger = new CapturingLogger<EmailTools>();
        var tools = new EmailTools(crm, knowledge, generator, new HttpContextAccessor(), logger);
        return (tools, crm, knowledge, generator, logger);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ---- §7.2 rows 1-5: input validation, pre-I/O ----

    [Fact]
    public async Task GenerateEmail_BlankCustomerId_ReturnsInvalidArgument()
    {
        var (tools, crm, _, _, _) = CreateTools();

        var result = await tools.GenerateEmail("   ", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(crm.LastLookupQuery);
    }

    [Fact]
    public async Task GenerateEmail_BlankObjective_ReturnsInvalidArgument()
    {
        var (tools, crm, _, _, _) = CreateTools();

        var result = await tools.GenerateEmail("CUS-0001", "   ", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(crm.LastLookupQuery);
    }

    [Fact]
    public async Task GenerateEmail_ObjectiveTooLong_ReturnsInvalidArgumentWithoutCallingCrmGateway()
    {
        var (tools, crm, _, _, _) = CreateTools();
        var tooLong = new string('a', 501);

        var result = await tools.GenerateEmail("CUS-0001", tooLong, cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(crm.LastLookupQuery);
    }

    [Theory]
    [InlineData("Professional")]
    [InlineData("")]
    [InlineData("friendly")]
    public async Task GenerateEmail_InvalidTone_ReturnsInvalidArgument(string tone)
    {
        var (tools, _, _, _, _) = CreateTools();

        var result = await tools.GenerateEmail("CUS-0001", "objective", tone, cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GenerateEmail_BlankButNonNullProductCode_ReturnsInvalidArgument()
    {
        var (tools, _, _, _, _) = CreateTools();

        var result = await tools.GenerateEmail("CUS-0001", "objective", productCode: "   ", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    // ---- Amendment A1: productCode format validation, before any I/O ----

    [Fact]
    public async Task GenerateEmail_ProductCodeTooLong_ReturnsInvalidArgumentWithoutCallingCrmGateway()
    {
        var (tools, crm, _, _, _) = CreateTools();
        var tooLong = "PRD-" + new string('X', 40); // 44 chars, over the 40-char cap

        var result = await tools.GenerateEmail("CUS-0001", "objective", productCode: tooLong, cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(crm.LastLookupQuery);
    }

    [Theory]
    [InlineData("prd-sav-006m")]
    [InlineData("PRD SAV 006M")]
    [InlineData("DROP TABLE products")]
    [InlineData("random-text")]
    public async Task GenerateEmail_ProductCodeInvalidFormat_ReturnsInvalidArgumentWithoutCallingCrmGateway(string productCode)
    {
        var (tools, crm, _, _, _) = CreateTools();

        var result = await tools.GenerateEmail("CUS-0001", "objective", productCode: productCode, cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(crm.LastLookupQuery);
    }

    [Fact]
    public async Task GenerateEmail_ProductCodeContainsEmailShapedText_ReturnsInvalidArgumentWithoutCallingCrmGateway()
    {
        var (tools, crm, _, _, _) = CreateTools();

        var result = await tools.GenerateEmail(
            "CUS-0001", "objective", productCode: "contact@example.com", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(crm.LastLookupQuery);
    }

    [Fact]
    public async Task GenerateEmail_ProductCodeContainsSecretTokenShapedText_ReturnsInvalidArgumentWithoutCallingCrmGateway()
    {
        var (tools, crm, _, _, _) = CreateTools();

        var result = await tools.GenerateEmail(
            "CUS-0001", "objective", productCode: "AIzaSyDaGmWKa4JsXZHjGw7ISLn3namB", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(crm.LastLookupQuery);
    }

    [Theory]
    [InlineData("PRD-SAV-006M")]
    [InlineData("PRD-SAV-012M")]
    [InlineData("PRD-CARD-CASHBACK-01")]
    [InlineData("PRD-LOAN-PERSONAL-01")]
    [InlineData("PRD-LOAN-HOME-01")]
    [InlineData("PRD-INS-LIFE-01")]
    public async Task GenerateEmail_ProductCodeCanonicalValid_PassesFormatValidation(string productCode)
    {
        var (tools, crm, _, _, _) = CreateTools();

        await tools.GenerateEmail("CUS-0001", "objective", productCode: productCode, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(crm.LastLookupQuery);
    }

    // ---- F3: null customerId audit robustness (HashForAudit must not throw) ----

    [Fact]
    public async Task GenerateEmail_NullCustomerId_ReturnsInvalidArgumentWithoutCallingCrmRagOrGemini()
    {
        var (tools, crm, knowledge, generator, _) = CreateTools();

        // null! — a real MCP client can send a JSON-RPC call with a null/missing customerId
        // argument despite the C# parameter being declared non-nullable; that annotation is a
        // compile-time contract only, not runtime-enforced. This proves the method returns a
        // clean INVALID_ARGUMENT instead of throwing inside HashForAudit's trailing audit log.
        var result = await tools.GenerateEmail(null!, "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(crm.LastLookupQuery);
        Assert.Null(knowledge.LastProductQuery);
        Assert.Null(knowledge.LastTemplateQuery);
        Assert.Equal(0, generator.CallCount);
    }

    // ---- §7.2 rows 6-12: customer/interaction/retrieval failures ----

    [Fact]
    public async Task GenerateEmail_CustomerNotFound_ReturnsStructuredNotFound()
    {
        var (tools, crm, _, _, _) = CreateTools();
        crm.FindCustomerResult = CustomerLookupResult.NotFound;

        var result = await tools.GenerateEmail("CUS-9999", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GenerateEmail_CustomerLookupAmbiguous_ReturnsInternalErrorAsDefensiveContractViolation()
    {
        var (tools, crm, _, _, _) = CreateTools();
        crm.FindCustomerResult = CustomerLookupResult.Ambiguous([new CustomerCandidateDto("CUS-0002", "Trần Thị Hương", "Priority", "Hà Nội")]);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InternalError, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GenerateEmail_FindCustomerThrowsCrmUpstreamException_ReturnsUpstreamUnavailable()
    {
        var (tools, crm, _, _, _) = CreateTools();
        crm.ThrowOnFindCustomer = new CrmUpstreamException("internal-detail-should-not-leak", retryable: true, traceId: null);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.UpstreamUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("internal-detail-should-not-leak", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmail_GetInteractionsThrowsCrmNotFoundException_ReturnsStructuredNotFound()
    {
        var (tools, crm, _, _, _) = CreateTools();
        crm.ThrowOnGetInteractions = new CrmNotFoundException("CUS-0001", traceId: null);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GenerateEmail_GetInteractionsThrowsCrmUpstreamException_ReturnsUpstreamUnavailable()
    {
        var (tools, crm, _, _, _) = CreateTools();
        crm.ThrowOnGetInteractions = new CrmUpstreamException("internal-detail-should-not-leak", retryable: false, traceId: null);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.UpstreamUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.GetProperty("error").GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task GenerateEmail_ProductRetrievalThrowsKnowledgeEmbeddingException_ReturnsRagUnavailable()
    {
        var (tools, _, knowledge, _, _) = CreateTools();
        knowledge.ThrowOnProductSearch = new KnowledgeEmbeddingException("internal-detail-should-not-leak", retryable: true);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.RagUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("internal-detail-should-not-leak", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmail_TemplateRetrievalThrowsKnowledgeVectorStoreException_ReturnsRagUnavailable()
    {
        var (tools, _, knowledge, _, _) = CreateTools();
        knowledge.ThrowOnTemplateSearch = new KnowledgeVectorStoreException("internal-detail-should-not-leak", retryable: false);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.RagUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.GetProperty("error").GetProperty("retryable").GetBoolean());
    }

    // ---- §7.2 rows 13-14: evidence-sufficiency short-circuits ----

    [Fact]
    public async Task GenerateEmail_NoProductOrTemplateEvidence_ReturnsNotFoundWithNullError()
    {
        var (tools, _, knowledge, generator, _) = CreateTools();
        knowledge.ProductResult = KnowledgeSearchResult.NoRelevantEvidence;
        knowledge.TemplateResult = KnowledgeSearchResult.NoRelevantEvidence;

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(0, generator.CallCount);
    }

    [Fact]
    public async Task GenerateEmail_RequestedProductCodeNotAmongRetrievedProductEvidence_ReturnsNotFoundWithNullError()
    {
        var (tools, _, knowledge, generator, _) = CreateTools();
        knowledge.ProductResult = KnowledgeSearchResult.Found([ProductMatchA]); // only PRD-SAV-006M

        var result = await tools.GenerateEmail(
            "CUS-0001", "objective", productCode: "PRD-LOAN-PERSONAL-01", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(0, generator.CallCount);
    }

    // ---- §6.4 / ✏️4a: retrieval query construction ----

    [Fact]
    public async Task GenerateEmail_CanonicalObjective_ProductRetrievalUsesTopK3AndTemplateUsesTopK2()
    {
        var (tools, _, knowledge, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw());

        await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, knowledge.LastProductQuery!.TopK);
        Assert.Equal(2, knowledge.LastTemplateQuery!.TopK);
    }

    [Fact]
    public async Task GenerateEmail_ProductCodeProvided_IncludedInRetrievalQueryText()
    {
        var (tools, _, knowledge, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw());

        await tools.GenerateEmail("CUS-0001", "objective", productCode: "PRD-SAV-006M", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("PRD-SAV-006M", knowledge.LastProductQuery!.QueryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmail_ObjectivePiiOnly_NeverReachesRetrievalQueryOrPromptUnmasked()
    {
        var (tools, _, knowledge, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw());
        const string rawEmail = "leaked.raw@example.com";

        await tools.GenerateEmail("CUS-0001", $"Liên hệ qua {rawEmail}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(rawEmail, knowledge.LastProductQuery!.QueryText, StringComparison.Ordinal);
        Assert.DoesNotContain(rawEmail, generator.LastContext!.MaskedObjective, StringComparison.Ordinal);
    }

    // ---- ✏️3: citation groundedness ----

    [Fact]
    public async Task GenerateEmail_ModelUsedSourceIdsEmpty_TriggersRetryThenModelError()
    {
        var (tools, _, _, generator, _) = CreateTools();
        var invalid = ValidRaw(usedSourceIds: []);
        generator.Results.Enqueue(invalid);
        generator.Results.Enqueue(invalid);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task GenerateEmail_ModelUsedSourceIdsOnlyInteractionNoKnowledgeSource_TriggersRetryThenModelError()
    {
        var (tools, _, _, generator, _) = CreateTools();
        var invalid = ValidRaw(usedSourceIds: ["crm:interaction:INT-0001"], suggestedProductCode: null);
        generator.Results.Enqueue(invalid);
        generator.Results.Enqueue(invalid);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task GenerateEmail_ModelUsedSourceIdsNotSubsetOfRetrieved_TriggersRetryThenModelError()
    {
        var (tools, _, _, generator, _) = CreateTools();
        var invalid = ValidRaw(usedSourceIds: ["kb:product:PRD-DOES-NOT-EXIST-01"], suggestedProductCode: null);
        generator.Results.Enqueue(invalid);
        generator.Results.Enqueue(invalid);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GenerateEmail_ModelUsedSourceIdsSubsetOfRetrieved_Succeeds()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw());

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
    }

    // ---- ✏️4c: requested productCode strict match ----

    [Fact]
    public async Task GenerateEmail_RequestedProductCodeButModelSuggestsDifferentCode_TriggersRetryThenModelError()
    {
        var (tools, _, knowledge, generator, _) = CreateTools();
        knowledge.ProductResult = KnowledgeSearchResult.Found([ProductMatchA, ProductMatchB]);
        var invalid = ValidRaw(usedSourceIds: ["kb:product:PRD-SAV-012M"], suggestedProductCode: "PRD-SAV-012M");
        generator.Results.Enqueue(invalid);
        generator.Results.Enqueue(invalid);

        var result = await tools.GenerateEmail(
            "CUS-0001", "objective", productCode: "PRD-SAV-006M", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(2, generator.CallCount);
    }

    // ---- Amendment A2: raw customer-name leak detection, before placeholder restore ----

    [Fact]
    public async Task GenerateEmail_ModelOutputsRawCustomerNameInSubjectInsteadOfPlaceholder_TriggersRetryThenModelError()
    {
        var (tools, _, _, generator, _) = CreateTools();
        var leaked = ValidRaw(subject: $"Thông tin gửi {Customer.FullName}", body: "Nội dung không có placeholder.");
        generator.Results.Enqueue(leaked);
        generator.Results.Enqueue(leaked);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task GenerateEmail_ModelOutputsRawCustomerNameInBodyInsteadOfPlaceholder_TriggersRetryThenModelError()
    {
        var (tools, _, _, generator, _) = CreateTools();
        var leaked = ValidRaw(body: $"Kính gửi {Customer.FullName}, đây là email tham khảo.");
        generator.Results.Enqueue(leaked);
        generator.Results.Enqueue(leaked);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task GenerateEmail_ModelRepeatsRawNameLeakOnBothAttempts_ReturnsModelErrorRetryableTrue()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw(subject: $"Gửi {Customer.FullName}"));
        generator.Results.Enqueue(ValidRaw(body: $"Kính gửi {Customer.FullName},"));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(2, generator.CallCount);
    }

    // ---- §7.2 rows 15-17: generation-call failures / invalid JSON / insufficient evidence ----

    [Fact]
    public async Task GenerateEmail_GeneratorThrowsEmailGenerationException_ReturnsModelErrorPreservingExceptionRetryable()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.ThrowOnGenerate = new EmailGenerationException(retryable: true, new InvalidOperationException("boom"));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain("boom", result, StringComparison.Ordinal);
        Assert.Equal(1, generator.CallCount); // a thrown exception is terminal, never retried
    }

    [Fact]
    public async Task GenerateEmail_GeneratorReturnsNullTwice_ReturnsModelErrorRetryableTrueAfterExactlyTwoAttempts()
    {
        var (tools, _, _, generator, _) = CreateTools();
        // Queue stays empty -> GenerateAsync returns null both times.

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.ModelError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task GenerateEmail_GeneratorReturnsNullThenValidOnRetry_SucceedsUsingSecondAttempt()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(null);
        generator.Results.Enqueue(ValidRaw());

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(2, generator.CallCount);
        Assert.NotNull(generator.LastContext!.CorrectiveInstruction);
    }

    [Fact]
    public async Task GenerateEmail_ModelReturnsInsufficientEvidence_ReturnsNotFoundWithNullError()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(InsufficientEvidenceRaw());

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(1, generator.CallCount); // terminal, never retried
    }

    // ---- F1: null model collections must be invalid-model-output (retry path), never INTERNAL_ERROR ----

    [Fact]
    public async Task GenerateEmail_ModelUsedSourceIdsNullTwice_ReturnsModelErrorAfterExactlyTwoAttempts()
    {
        var (tools, _, _, generator, _) = CreateTools();
        var invalid = new RawEmailDraftModel(
            RawEmailDraftModel.StatusOk, "subject", "body", "PRD-SAV-006M", null, true, []);
        generator.Results.Enqueue(invalid);
        generator.Results.Enqueue(invalid);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        var errorCode = root.GetProperty("error").GetProperty("code").GetString();
        Assert.Equal(McpToolErrorCode.ModelError, errorCode);
        Assert.NotEqual(McpToolErrorCode.InternalError, errorCode);
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task GenerateEmail_ModelWarningsNullTwice_ReturnsModelErrorAfterExactlyTwoAttempts()
    {
        var (tools, _, _, generator, _) = CreateTools();
        var invalid = new RawEmailDraftModel(
            RawEmailDraftModel.StatusOk, "subject", "body", "PRD-SAV-006M", ["kb:product:PRD-SAV-006M"], true, null);
        generator.Results.Enqueue(invalid);
        generator.Results.Enqueue(invalid);

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        var errorCode = root.GetProperty("error").GetProperty("code").GetString();
        Assert.Equal(McpToolErrorCode.ModelError, errorCode);
        Assert.NotEqual(McpToolErrorCode.InternalError, errorCode);
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(2, generator.CallCount);
    }

    [Fact]
    public async Task GenerateEmail_ModelUsedSourceIdsNullFirstAttemptThenValidSecondAttempt_Succeeds()
    {
        var (tools, _, _, generator, _) = CreateTools();
        var invalid = new RawEmailDraftModel(
            RawEmailDraftModel.StatusOk, "subject", "body", "PRD-SAV-006M", null, true, []);
        generator.Results.Enqueue(invalid);
        generator.Results.Enqueue(ValidRaw());

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(2, generator.CallCount);
        Assert.NotNull(generator.LastContext!.CorrectiveInstruction);
    }

    // ---- §7.2 row 18 + shape assertions ----

    [Fact]
    public async Task GenerateEmail_Success_ReturnsFullDraftShapeMatchingDoc07Example()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw(usedSourceIds: ["kb:product:PRD-SAV-006M", "kb:email-template:TPL-EMAIL-MATURITY-01"]));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        var draft = root.GetProperty("data").GetProperty("draft");
        Assert.True(draft.GetProperty("subject").GetString()!.Length > 0);
        Assert.True(draft.GetProperty("body").GetString()!.Length > 0);
        Assert.Equal("PRD-SAV-006M", draft.GetProperty("suggestedProductCode").GetString());
        Assert.True(draft.GetProperty("sourceIds").GetArrayLength() > 0);
        Assert.True(draft.GetProperty("requiresHumanApproval").GetBoolean());
        Assert.True(draft.GetProperty("piiMaskSummary").GetProperty("maskedFieldTypes").GetArrayLength() >= 4);
    }

    [Fact]
    public async Task GenerateEmail_Success_RequiresHumanApprovalAlwaysTrueEvenIfModelReturnedFalse()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw(requiresHumanApproval: false));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.True(root.GetProperty("data").GetProperty("draft").GetProperty("requiresHumanApproval").GetBoolean());
    }

    // ---- ✏️5: placeholder restore ----

    [Fact]
    public async Task GenerateEmail_PlaceholderPresent_RestoredWithCustomerFullName()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw(body: "Kính gửi {{CUSTOMER_NAME}}, nội dung email."));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        var body = root.GetProperty("data").GetProperty("draft").GetProperty("body").GetString()!;
        Assert.Contains(Customer.FullName, body, StringComparison.Ordinal);
        Assert.DoesNotContain("{{CUSTOMER_NAME}}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmail_PlaceholderInSubject_RestoredWithCustomerFullName()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw(subject: "Thông tin dành cho {{CUSTOMER_NAME}}"));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        var subject = root.GetProperty("data").GetProperty("draft").GetProperty("subject").GetString()!;
        Assert.Contains(Customer.FullName, subject, StringComparison.Ordinal);
        Assert.DoesNotContain("{{CUSTOMER_NAME}}", subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateEmail_PlaceholderMissingFromModelOutput_BodyGetsNeutralGreeting_SubjectUnchanged()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw(subject: "Thông tin sản phẩm", body: "Nội dung email không có placeholder."));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        var draft = root.GetProperty("data").GetProperty("draft");
        Assert.Equal("Thông tin sản phẩm", draft.GetProperty("subject").GetString());
        Assert.StartsWith("Kính gửi Anh/Chị,\n\nNội dung email không có placeholder.", draft.GetProperty("body").GetString());
    }

    // ---- finalSourceIds construction ----

    [Fact]
    public async Task GenerateEmail_FinalSourceIds_DedupedAndOrderedByRetrievalRankNotModelOrder()
    {
        var (tools, _, knowledge, generator, _) = CreateTools();
        knowledge.ProductResult = KnowledgeSearchResult.Found([ProductMatchA, ProductMatchB]);
        generator.Results.Enqueue(ValidRaw(usedSourceIds:
        [
            "kb:email-template:TPL-EMAIL-MATURITY-01",
            "crm:interaction:INT-0001",
            "kb:product:PRD-SAV-006M",
            "kb:product:PRD-SAV-006M", // duplicate
        ]));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        var sourceIds = root.GetProperty("data").GetProperty("draft").GetProperty("sourceIds")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(
            ["kb:product:PRD-SAV-006M", "kb:email-template:TPL-EMAIL-MATURITY-01", "crm:interaction:INT-0001"],
            sourceIds);
    }

    [Fact]
    public async Task GenerateEmail_FinalSourceIds_CanIncludeInteractionSourceId()
    {
        var (tools, _, _, generator, _) = CreateTools();
        generator.Results.Enqueue(ValidRaw(usedSourceIds: ["kb:product:PRD-SAV-006M", "crm:interaction:INT-0001"]));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        var sourceIds = root.GetProperty("data").GetProperty("draft").GetProperty("sourceIds")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("crm:interaction:INT-0001", sourceIds);
    }

    // ---- ✏️6a: warnings never logged raw ----

    [Fact]
    public async Task GenerateEmail_ModelWarningsPresent_OnlyWarningCountLoggedNeverRawWarningText()
    {
        var (tools, _, _, generator, logger) = CreateTools();
        const string rawWarningText = "some very specific model-generated warning text";
        generator.Results.Enqueue(ValidRaw(warnings: [rawWarningText]));

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.False(root.GetProperty("data").GetProperty("draft").TryGetProperty("warnings", out _));
        Assert.Contains("warningCount=1", logger.AllCapturedText());
        Assert.DoesNotContain(rawWarningText, logger.AllCapturedText(), StringComparison.Ordinal);
    }

    // ---- §7.2 rows 20-21: defensive/catch-all ----

    [Fact]
    public async Task GenerateEmail_UnhandledException_ReturnsInternalErrorWithoutLeakingMessage()
    {
        var (tools, crm, _, _, logger) = CreateTools();
        crm.ThrowOnFindCustomer = new InvalidOperationException("internal-detail-should-not-leak");

        var result = await tools.GenerateEmail("CUS-0001", "objective", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InternalError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("internal-detail-should-not-leak", result, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-detail-should-not-leak", logger.AllCapturedText(), StringComparison.Ordinal);
    }

    // ---- ✏️30: cancellation ----

    [Fact]
    public async Task GenerateEmail_CallerCancelsRequest_ThrowsOperationCanceledExceptionNotWrappedAsInternalError()
    {
        // The fakes are synchronous stand-ins that never observe the token themselves, so
        // cancellation is simulated the same way other failures are: the generator throws it, and
        // the assertion is that EmailTools's own `when (cancellationToken.IsCancellationRequested)`
        // clause rethrows rather than wrapping it as INTERNAL_ERROR — the guard reads the method's
        // own cancellationToken parameter, not the thrown exception's token.
        var (tools, _, _, generator, _) = CreateTools();
        generator.ThrowOnGenerate = new OperationCanceledException();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tools.GenerateEmail("CUS-0001", "objective", cancellationToken: cts.Token));
    }

    // ---- ✏️7: ReadOnly annotation is verified at the protocol level (McpToolProtocolTests) ----
}
