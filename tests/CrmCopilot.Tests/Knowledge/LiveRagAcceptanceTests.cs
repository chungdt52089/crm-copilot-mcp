using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrmCopilot.Tests.Knowledge;

/// <summary>
/// Opt-in — never runs as part of the default offline suite (CLAUDE.md §5/PD-019) — but its
/// output is required checkpoint evidence for P0-03, not merely optional (plan §16). Uses an
/// isolated "crm-copilot-knowledge-livetest" collection so it never touches the default dev
/// "crm-copilot-knowledge" collection. Reports as Skipped, not Passed, when credentials are
/// absent — the completion report must cite the actual skip/run status, never claim this ran
/// without a real GEMINI_API_KEY and reachable CHROMA_BASE_URL.
/// </summary>
public class LiveRagAcceptanceTests
{
    private const string LiveTestCollectionName = "crm-copilot-knowledge-livetest";

    public static bool LiveRagCredentialsPresent =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GeminiEmbeddingOptions.ApiKeyConfigKey)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ChromaOptions.BaseUrlConfigKey));

    [Fact(
        SkipUnless = nameof(LiveRagCredentialsPresent),
        Skip = "Opt-in live acceptance test: requires a real GEMINI_API_KEY and a reachable CHROMA_BASE_URL " +
               "(README's 'Mandatory live acceptance run'). Not run by default/CI.")]
    public async Task Heartbeat_Ingestion_Idempotency_And_CanonicalRetrieval_AgainstRealGeminiAndChroma()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddKnowledgeRetrieval(configuration);
        services.PostConfigure<ChromaOptions>(options => options.CollectionName = LiveTestCollectionName);
        using var provider = services.BuildServiceProvider();

        var vectorStore = provider.GetRequiredService<IVectorStore>();
        var heartbeat = await vectorStore.HeartbeatAsync(TestContext.Current.CancellationToken);
        Assert.True(heartbeat, "Chroma heartbeat failed — is the container running and CHROMA_BASE_URL correct?");

        var documents = KnowledgeSourceLoader.LoadFromAppBaseDirectory();
        var ingestionService = provider.GetRequiredService<KnowledgeIngestionService>();

        var firstRun = await ingestionService.IngestAsync(documents, TestContext.Current.CancellationToken);
        Assert.Equal(14, firstRun.TotalDocuments);
        Assert.Equal(14, await vectorStore.CountAsync(TestContext.Current.CancellationToken));

        // Second run over unchanged source data: zero new embedding calls, still 14 records —
        // the literal idempotency proof (plan §16/item 8), not just "no duplicate rows".
        var secondRun = await ingestionService.IngestAsync(documents, TestContext.Current.CancellationToken);
        Assert.Equal(0, secondRun.Embedded);
        Assert.Equal(14, secondRun.Skipped);
        Assert.Equal(14, await vectorStore.CountAsync(TestContext.Current.CancellationToken));

        var retriever = provider.GetRequiredService<IKnowledgeRetriever>();
        var result = await retriever.SearchAsync(
            new KnowledgeSearchQuery("Khách hàng quan tâm gửi tiết kiệm an toàn kỳ hạn 6 tháng, cần liên hệ lại."),
            TestContext.Current.CancellationToken);

        Assert.Equal(KnowledgeSearchStatus.Found, result.Status);
        Assert.Contains(result.Matches, match => match.SourceId == "kb:product:PRD-SAV-006M");

        var embeddingClient = provider.GetRequiredService<IEmbeddingClient>();
        var embedding = await embeddingClient.EmbedQueryAsync("norm check", TestContext.Current.CancellationToken);
        Assert.Equal(GeminiEmbeddingOptions.Dimension, embedding.Length);
        var norm = Math.Sqrt(embedding.Sum(value => value * value));
        Assert.True(Math.Abs(norm - 1.0) < 0.01, $"Expected L2 norm ~1.0 for a real Gemini embedding, got {norm}.");
    }
}
