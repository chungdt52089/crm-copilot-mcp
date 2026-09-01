using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;
using CrmCopilot.Tests.Knowledge.TestSupport;

namespace CrmCopilot.Tests.Knowledge;

public class KnowledgeIngestionServiceTests
{
    private static List<KnowledgeSourceDocument> ThreeDocuments() =>
    [
        new("kb:product:PRD-01", KnowledgeDocumentType.Product, "Product one text", "PRD-01", null, "vi", "1.0"),
        new("kb:product:PRD-02", KnowledgeDocumentType.Product, "Product two text", "PRD-02", null, "vi", "1.0"),
        new("kb:email-template:TPL-01", KnowledgeDocumentType.EmailTemplate, "Template one text", null, "TPL-01", "vi", "1.0"),
    ];

    [Fact]
    public async Task IngestAsync_FirstRun_EmbedsEveryDocument()
    {
        var embeddingClient = new FakeEmbeddingClient();
        var vectorStore = new FakeVectorStore();
        var service = new KnowledgeIngestionService(embeddingClient, vectorStore);

        var summary = await service.IngestAsync(ThreeDocuments(), TestContext.Current.CancellationToken);

        Assert.Equal(3, summary.TotalDocuments);
        Assert.Equal(3, summary.Embedded);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(3, embeddingClient.DocumentCallCount);
        Assert.Equal(3, vectorStore.RecordCount);
    }

    [Fact]
    public async Task IngestAsync_SecondRunUnchanged_MakesZeroEmbeddingCallsAndNoDuplicates()
    {
        var embeddingClient = new FakeEmbeddingClient();
        var vectorStore = new FakeVectorStore();
        var service = new KnowledgeIngestionService(embeddingClient, vectorStore);
        var documents = ThreeDocuments();

        await service.IngestAsync(documents, TestContext.Current.CancellationToken);
        var callCountAfterFirstRun = embeddingClient.DocumentCallCount;

        var secondSummary = await service.IngestAsync(documents, TestContext.Current.CancellationToken);

        Assert.Equal(0, secondSummary.Embedded);
        Assert.Equal(3, secondSummary.Skipped);
        Assert.Equal(callCountAfterFirstRun, embeddingClient.DocumentCallCount); // zero *new* embedding calls
        Assert.Equal(3, vectorStore.RecordCount); // still exactly 3 — no duplicates
    }

    [Fact]
    public async Task IngestAsync_OneDocumentContentChanged_OnlyReEmbedsThatOne()
    {
        var embeddingClient = new FakeEmbeddingClient();
        var vectorStore = new FakeVectorStore();
        var service = new KnowledgeIngestionService(embeddingClient, vectorStore);
        var documents = ThreeDocuments();

        await service.IngestAsync(documents, TestContext.Current.CancellationToken);

        var changed = documents.ToList();
        changed[0] = changed[0] with { RenderedText = "Product one text — updated" };

        var secondSummary = await service.IngestAsync(changed, TestContext.Current.CancellationToken);

        Assert.Equal(1, secondSummary.Embedded);
        Assert.Equal(2, secondSummary.Skipped);
        Assert.Equal(3, vectorStore.RecordCount);
    }

    [Fact]
    public async Task IngestAsync_StoredFingerprintFromDifferentModelDimensionMetric_ReEmbedsDespiteUnchangedText()
    {
        var embeddingClient = new FakeEmbeddingClient();
        var vectorStore = new FakeVectorStore();
        var service = new KnowledgeIngestionService(embeddingClient, vectorStore);

        var document = new KnowledgeSourceDocument("kb:product:PRD-01", KnowledgeDocumentType.Product, "same rendered text", "PRD-01", null, "vi", "1.0");

        // Seed a record as if ingested under a different (old) model/dimension/metric —
        // reproduces exactly what a real embedding-model bump leaves behind: unchanged rendered
        // text, but a fingerprint computed under constants that no longer match
        // GeminiEmbeddingOptions/ChromaOptions' current values.
        var staleFingerprint = KnowledgeFingerprint.Compute(document.RenderedText, "gemini-embedding-000-old", 512, "cosine");
        var staleMetadata = new KnowledgeSourceMetadata(
            document.SourceId, document.DocumentType, document.ProductCode, document.TemplateId,
            document.Language, document.Version, "gemini-embedding-000-old", 512, "cosine", true, staleFingerprint);
        await vectorStore.UpsertAsync(
            new VectorRecord(document.SourceId, FakeEmbeddingClient.DeterministicVector("old"), "old text", staleMetadata),
            TestContext.Current.CancellationToken);

        var summary = await service.IngestAsync([document], TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.Embedded);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(1, embeddingClient.DocumentCallCount);
    }
}
