using System.Text.Json;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Crm;
using CrmCopilot.McpServer.Email;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.Email;

/// <summary>
/// Opt-in — never runs as part of the default offline suite (CLAUDE.md §5/PD-019), mirrors
/// LiveRagAcceptanceTests.cs exactly. Uses the isolated "crm-copilot-knowledge-livetest" collection
/// so it never touches the default dev "crm-copilot-knowledge" collection. This is evidence *in
/// support of* the mandatory manual live acceptance run (P0-07 plan §9.1), not a substitute for
/// it — reports Skipped, not Passed, when credentials/MockCrmApi are absent.
/// </summary>
public class LiveEmailGenerationAcceptanceTests
{
    private const string LiveTestCollectionName = "crm-copilot-knowledge-livetest";

    public static bool LiveEmailGenerationCredentialsPresent =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GeminiEmbeddingOptions.ApiKeyConfigKey)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ChromaOptions.BaseUrlConfigKey)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MockCrmGatewayOptions.ConfigKey));

    [Fact(
        SkipUnless = nameof(LiveEmailGenerationCredentialsPresent),
        Skip = "Opt-in live acceptance test: requires a real GEMINI_API_KEY, a reachable CHROMA_BASE_URL, and a " +
               "running CrmCopilot.MockCrmApi at MOCKCRM_API_BASE_URL (P0-07 plan §9's mandatory live acceptance " +
               "run). Not run by default/CI.")]
    public async Task GenerateEmail_CanonicalObjective_AgainstRealGeminiChromaAndMockCrmApi()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddCrmGateway(configuration);
        services.AddKnowledgeRetrieval(configuration);
        services.AddEmailGeneration();
        services.PostConfigure<ChromaOptions>(options => options.CollectionName = LiveTestCollectionName);
        using var provider = services.BuildServiceProvider();

        var documents = KnowledgeSourceLoader.LoadFromAppBaseDirectory();
        var ingestionService = provider.GetRequiredService<KnowledgeIngestionService>();
        await ingestionService.IngestAsync(documents, TestContext.Current.CancellationToken);

        var crmGateway = provider.GetRequiredService<ICrmGateway>();
        var knowledgeRetriever = provider.GetRequiredService<IKnowledgeRetriever>();
        var emailDraftGenerator = provider.GetRequiredService<IEmailDraftGenerator>();
        var tools = new EmailTools(crmGateway, knowledgeRetriever, emailDraftGenerator, new HttpContextAccessor(), NullLogger<EmailTools>.Instance);

        var result = await tools.GenerateEmail(
            "CUS-0001",
            "Follow-up nhu cầu gửi tiết kiệm kỳ hạn 6 tháng",
            "professional_warm",
            null,
            TestContext.Current.CancellationToken);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal("success", root.GetProperty("status").GetString());

        var draft = root.GetProperty("data").GetProperty("draft");
        Assert.True(draft.GetProperty("requiresHumanApproval").GetBoolean());
        Assert.True(draft.GetProperty("sourceIds").GetArrayLength() > 0);

        var body = draft.GetProperty("body").GetString()!;
        // The final body must never contain the raw literal placeholder token — it must contain
        // either the real synthetic name (restored) or the neutral greeting fallback (doc08 §6),
        // never an unconditional "must contain the real name" claim (P0-07 plan Revision 2 ✏️5b).
        Assert.DoesNotContain("{{CUSTOMER_NAME}}", body, StringComparison.Ordinal);
    }
}
