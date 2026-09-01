using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        // Isolation guard, asserted BEFORE anything is written. LiveTestCollectionName is a
        // compile-time constant, but PostConfigure silently doing nothing would send every upsert
        // below into the default dev collection instead. Proving the resolved name here is what
        // makes "this test never touches crm-copilot-knowledge" a checked fact rather than an
        // assumption.
        var resolvedCollection = provider.GetRequiredService<IOptions<ChromaOptions>>().Value.CollectionName;
        Assert.Equal(LiveTestCollectionName, resolvedCollection);
        Assert.NotEqual(ChromaOptions.DefaultCollectionName, resolvedCollection);

        var vectorStore = provider.GetRequiredService<IVectorStore>();
        var heartbeat = await vectorStore.HeartbeatAsync(TestContext.Current.CancellationToken);
        Assert.True(heartbeat, "Chroma heartbeat failed — is the container running and CHROMA_BASE_URL correct?");

        var documents = KnowledgeSourceLoader.LoadFromAppBaseDirectory();
        var ingestionService = provider.GetRequiredService<KnowledgeIngestionService>();

        // The expected count is derived from the corpus, not hard-coded: the corpus legitimately
        // grows (P0-03 shipped 14 documents; P0-10 added 7 call-script playbooks for 21). A literal
        // would have to be edited on every such change and says nothing about correctness.
        //
        // Deriving it does mean documents.Count could itself be wrong, so the two properties a
        // literal used to imply are now asserted directly instead of assumed:
        //   - the corpus is non-trivial (a loader returning nothing must not pass vacuously);
        //   - every source id is unique (a duplicated document must not inflate the count).
        Assert.NotEmpty(documents);
        Assert.Equal(documents.Count, documents.Select(document => document.SourceId).Distinct(StringComparer.Ordinal).Count());

        // All three document types must be present — catches a loader that silently drops a file,
        // which a bare count could not distinguish from a smaller corpus.
        Assert.Contains(documents, document => document.DocumentType == KnowledgeDocumentType.Product);
        Assert.Contains(documents, document => document.DocumentType == KnowledgeDocumentType.EmailTemplate);
        Assert.Contains(documents, document => document.DocumentType == KnowledgeDocumentType.CallScript);

        var expectedRecordCount = documents.Count;

        var firstRun = await ingestionService.IngestAsync(documents, TestContext.Current.CancellationToken);
        Assert.Equal(expectedRecordCount, firstRun.TotalDocuments);

        var countAfterFirstRun = await vectorStore.CountAsync(TestContext.Current.CancellationToken);
        Assert.True(
            countAfterFirstRun == expectedRecordCount,
            $"Expected {expectedRecordCount} vectors in the isolated collection '{LiveTestCollectionName}' but found " +
            $"{countAfterFirstRun}. A higher count means vectors from an earlier, larger corpus are still present — " +
            $"upsert is by stable id and never deletes. Drop ONLY the isolated collection " +
            $"'{LiveTestCollectionName}' and re-run; never touch '{ChromaOptions.DefaultCollectionName}'.");

        // Second run over unchanged source data: zero new embedding calls, same record count — the
        // literal idempotency proof (plan §16/item 8), not just "no duplicate rows".
        var secondRun = await ingestionService.IngestAsync(documents, TestContext.Current.CancellationToken);
        Assert.Equal(0, secondRun.Embedded);
        Assert.Equal(expectedRecordCount, secondRun.Skipped);
        Assert.Equal(expectedRecordCount, secondRun.TotalDocuments);
        Assert.Equal(countAfterFirstRun, await vectorStore.CountAsync(TestContext.Current.CancellationToken));

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
