using System.Text.Json;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.CallScript;
using CrmCopilot.McpServer.Crm;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.CallScript;

/// <summary>
/// Opt-in — never runs as part of the default offline suite (CLAUDE.md §5/PD-019), mirrors
/// LiveEmailGenerationAcceptanceTests. Uses the isolated "crm-copilot-knowledge-livetest"
/// collection so it never touches the default dev "crm-copilot-knowledge" collection.
///
/// This is evidence *in support of* the mandatory manual live acceptance run, not a substitute for
/// it — it reports Skipped, not Passed, when credentials/MockCrmApi are absent, and a skip can
/// never be reported as a pass.
///
/// Self-seeding by design (plan Amendment ✏️10): it ingests the full knowledge corpus itself rather
/// than assuming a previous run left the collection populated, and asserts the second ingest embeds
/// nothing — so idempotence is proven by this test rather than asserted in prose elsewhere.
/// </summary>
public class LiveCallScriptGenerationAcceptanceTests
{
    private const string LiveTestCollectionName = "crm-copilot-knowledge-livetest";
    private const string CanonicalCustomerId = "CUS-0001";
    private const string CanonicalObjective = "Trao đổi với khách hàng về nhu cầu gửi tiết kiệm kỳ hạn 6 tháng";

    public static bool LiveCallScriptCredentialsPresent =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GeminiEmbeddingOptions.ApiKeyConfigKey)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ChromaOptions.BaseUrlConfigKey)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MockCrmGatewayOptions.ConfigKey));

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddCrmGateway(configuration);
        services.AddKnowledgeRetrieval(configuration);
        services.AddCallScriptGeneration();
        services.PostConfigure<ChromaOptions>(options => options.CollectionName = LiveTestCollectionName);
        return services.BuildServiceProvider();
    }

    private static CallScriptTools CreateTools(ServiceProvider provider) => new(
        provider.GetRequiredService<ICrmGateway>(),
        provider.GetRequiredService<IKnowledgeRetriever>(),
        provider.GetRequiredService<ICallScriptGenerator>(),
        provider.GetRequiredService<ICallScriptTemplateCatalog>(),
        new HttpContextAccessor(),
        NullLogger<CallScriptTools>.Instance);

    [Fact(
        SkipUnless = nameof(LiveCallScriptCredentialsPresent),
        Skip = "Opt-in live acceptance test: requires a real GEMINI_API_KEY, a reachable CHROMA_BASE_URL, and a " +
               "running CrmCopilot.MockCrmApi at MOCKCRM_API_BASE_URL (P0-10 plan Amendment 10). Not run by default/CI.")]
    public async Task GenerateCallScript_SelfSeedsIsolatedCollection_ProvesIdempotenceAndGeneratesGroundedDraft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = BuildProvider();

        // --- seed the isolated collection, and prove the second run embeds nothing -------------
        var documents = KnowledgeSourceLoader.LoadFromAppBaseDirectory();
        var ingestionService = provider.GetRequiredService<KnowledgeIngestionService>();

        var firstRun = await ingestionService.IngestAsync(documents, cancellationToken);
        var secondRun = await ingestionService.IngestAsync(documents, cancellationToken);

        Assert.Equal(documents.Count, firstRun.TotalDocuments);
        Assert.Equal(0, secondRun.Embedded);
        Assert.Equal(firstRun.TotalDocuments, secondRun.Skipped);

        // --- both evidence kinds must actually be retrievable before the tool is exercised -----
        var retriever = provider.GetRequiredService<IKnowledgeRetriever>();

        var callScriptProbe = await retriever.SearchAsync(
            new KnowledgeSearchQuery(CanonicalObjective, [KnowledgeDocumentType.CallScript], 2), cancellationToken);
        var productProbe = await retriever.SearchAsync(
            new KnowledgeSearchQuery(CanonicalObjective, [KnowledgeDocumentType.Product], 3), cancellationToken);

        Assert.Equal(KnowledgeSearchStatus.Found, callScriptProbe.Status);
        Assert.Equal(KnowledgeSearchStatus.Found, productProbe.Status);

        // --- canonical call, with an explicit objective ----------------------------------------
        var tools = CreateTools(provider);
        var result = await tools.GenerateCallScript(CanonicalCustomerId, CanonicalObjective, null, null, cancellationToken);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());

        var draft = root.GetProperty("data").GetProperty("draft");
        Assert.True(draft.GetProperty("requiresHumanApproval").GetBoolean());
        Assert.True(draft.GetProperty("discoveryQuestions").GetArrayLength() > 0);
        Assert.True(draft.GetProperty("talkingPoints").GetArrayLength() > 0);
        Assert.False(string.IsNullOrWhiteSpace(draft.GetProperty("opening").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(draft.GetProperty("closing").GetString()));
        Assert.Equal(CallScriptObjectiveSources.Request, draft.GetProperty("objectiveSource").GetString());

        var sourceIds = draft.GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()!).ToList();

        // At least one knowledge source, which ValidateOkDraft guarantees. Asserting that BOTH a
        // product and a playbook appear would be asserting a model choice, not a system guarantee —
        // sources now report only what the draft actually cited, so a well-grounded draft that
        // leaned on just one of the two is correct, not a failure.
        Assert.Contains(
            sourceIds,
            id => id.StartsWith("kb:product:", StringComparison.Ordinal) || id.StartsWith("kb:call-script:", StringComparison.Ordinal));

        // The opportunity IS a system guarantee here: OPP-0002 is an Open opportunity for
        // PRD-SAV-006M, the product this canonical objective is about, so it is corroborated and
        // server-forced regardless of whether the model cited it.
        Assert.Contains(sourceIds, id => id.StartsWith("crm:opportunity:", StringComparison.Ordinal));

        Assert.Equal(sourceIds.Count, sourceIds.Distinct(StringComparer.Ordinal).Count());

        // No retrieval candidate may be reported unless the draft used it: every non-opportunity
        // source must be one the model actually cited.
        Assert.All(
            sourceIds.Where(id => !id.StartsWith("crm:opportunity:", StringComparison.Ordinal)),
            id => Assert.Contains(id, draft.GetProperty("sourceIds").EnumerateArray().Select(e => e.GetString()!)));

        // The raw placeholder token must never survive into the RM-facing text.
        Assert.DoesNotContain("{{CUSTOMER_NAME}}", draft.GetProperty("opening").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The short-sentence demo the Product Owner asked for: "Soạn kịch bản gọi cho khách hàng
    /// CUS-0001" carries no objective at all, so the tool must derive one and say that it did.
    /// </summary>
    [Fact(
        SkipUnless = nameof(LiveCallScriptCredentialsPresent),
        Skip = "Opt-in live acceptance test: requires a real GEMINI_API_KEY, a reachable CHROMA_BASE_URL, and a " +
               "running CrmCopilot.MockCrmApi at MOCKCRM_API_BASE_URL (P0-10 plan Amendment 10). Not run by default/CI.")]
    public async Task GenerateCallScript_WithoutObjective_InfersOneAndFlagsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = BuildProvider();

        var documents = KnowledgeSourceLoader.LoadFromAppBaseDirectory();
        await provider.GetRequiredService<KnowledgeIngestionService>().IngestAsync(documents, cancellationToken);

        var tools = CreateTools(provider);
        var result = await tools.GenerateCallScript(CanonicalCustomerId, null, null, null, cancellationToken);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());

        var draft = root.GetProperty("data").GetProperty("draft");
        Assert.False(string.IsNullOrWhiteSpace(draft.GetProperty("resolvedObjective").GetString()));
        Assert.Contains(
            CallScriptWarnings.ObjectiveInferred,
            draft.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("OPP-0002", draft.GetProperty("selectedOpportunityId").GetString());
    }
}
