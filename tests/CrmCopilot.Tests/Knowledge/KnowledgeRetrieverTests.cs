using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.Tests.Knowledge.TestSupport;
using Microsoft.Extensions.Options;

namespace CrmCopilot.Tests.Knowledge;

public class KnowledgeRetrieverTests
{
    private static KnowledgeSourceMetadata SampleMetadata(string sourceId, KnowledgeDocumentType type) => new(
        sourceId, type, type == KnowledgeDocumentType.Product ? "PRD-01" : null, type == KnowledgeDocumentType.EmailTemplate ? "TPL-01" : null,
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fingerprint");

    private static KnowledgeRetriever CreateRetriever(
        FakeEmbeddingClient embeddingClient, FakeVectorStore vectorStore, double maxDistance = 1.2) =>
        new(embeddingClient, vectorStore, Options.Create(new KnowledgeRetrievalOptions { MaxDistance = maxDistance }));

    [Fact]
    public async Task SearchAsync_MatchWithinThreshold_ReturnsFoundWithExpectedSource()
    {
        var vectorStore = new FakeVectorStore
        {
            QueryResultOverride =
            [
                new VectorMatch("kb:product:PRD-SAV-006M", "rendered text", SampleMetadata("kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product), Distance: 0.3),
            ],
        };
        var retriever = CreateRetriever(new FakeEmbeddingClient(), vectorStore);

        var result = await retriever.SearchAsync(new KnowledgeSearchQuery("khách hàng cần gửi tiết kiệm an toàn kỳ hạn 6 tháng"), TestContext.Current.CancellationToken);

        Assert.Equal(KnowledgeSearchStatus.Found, result.Status);
        Assert.Contains(result.Matches, m => m.SourceId == "kb:product:PRD-SAV-006M");
    }

    [Fact]
    public async Task SearchAsync_AllMatchesBeyondThreshold_ReturnsNoRelevantEvidence()
    {
        var vectorStore = new FakeVectorStore
        {
            QueryResultOverride =
            [
                new VectorMatch("kb:product:PRD-01", "text", SampleMetadata("kb:product:PRD-01", KnowledgeDocumentType.Product), Distance: 3.5),
            ],
        };
        var retriever = CreateRetriever(new FakeEmbeddingClient(), vectorStore, maxDistance: 1.2);

        var result = await retriever.SearchAsync(new KnowledgeSearchQuery("unrelated query text"), TestContext.Current.CancellationToken);

        Assert.Equal(KnowledgeSearchStatus.NoRelevantEvidence, result.Status);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task SearchAsync_EmptyMatches_ReturnsNoRelevantEvidence()
    {
        var vectorStore = new FakeVectorStore { QueryResultOverride = [] };
        var retriever = CreateRetriever(new FakeEmbeddingClient(), vectorStore);

        var result = await retriever.SearchAsync(new KnowledgeSearchQuery("query"), TestContext.Current.CancellationToken);

        Assert.Equal(KnowledgeSearchStatus.NoRelevantEvidence, result.Status);
    }

    [Fact]
    public async Task SearchAsync_DocumentTypeFilter_MapsToWireFormatAndForwardsToVectorStore()
    {
        var vectorStore = new FakeVectorStore { QueryResultOverride = [] };
        var retriever = CreateRetriever(new FakeEmbeddingClient(), vectorStore);

        await retriever.SearchAsync(
            new KnowledgeSearchQuery("query", DocumentTypes: [KnowledgeDocumentType.EmailTemplate]), TestContext.Current.CancellationToken);

        Assert.Equal(["email_template"], vectorStore.LastDocumentTypeFilter);
    }

    [Fact]
    public async Task SearchAsync_NoDocumentTypeFilterSpecified_ForwardsNullFilter()
    {
        var vectorStore = new FakeVectorStore { QueryResultOverride = [] };
        var retriever = CreateRetriever(new FakeEmbeddingClient(), vectorStore);

        await retriever.SearchAsync(new KnowledgeSearchQuery("query"), TestContext.Current.CancellationToken);

        Assert.Null(vectorStore.LastDocumentTypeFilter);
    }

    [Fact]
    public async Task SearchAsync_TopK_IsForwardedToVectorStore()
    {
        var vectorStore = new FakeVectorStore { QueryResultOverride = [] };
        var retriever = CreateRetriever(new FakeEmbeddingClient(), vectorStore);

        await retriever.SearchAsync(new KnowledgeSearchQuery("query", TopK: 5), TestContext.Current.CancellationToken);

        Assert.Equal(5, vectorStore.LastRequestedTopK);
    }

    [Fact]
    public async Task SearchAsync_BlankQueryText_ThrowsWithoutCallingEmbeddingClient()
    {
        var embeddingClient = new FakeEmbeddingClient();
        var retriever = CreateRetriever(embeddingClient, new FakeVectorStore());

        await Assert.ThrowsAsync<ArgumentException>(
            () => retriever.SearchAsync(new KnowledgeSearchQuery("   "), TestContext.Current.CancellationToken));

        Assert.Equal(0, embeddingClient.QueryCallCount);
    }

    [Fact]
    public async Task SearchAsync_QueryTextTooLong_Throws()
    {
        var retriever = CreateRetriever(new FakeEmbeddingClient(), new FakeVectorStore());
        var tooLong = new string('a', 2001);

        await Assert.ThrowsAsync<ArgumentException>(
            () => retriever.SearchAsync(new KnowledgeSearchQuery(tooLong), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task SearchAsync_TopKOutOfRange_Throws(int topK)
    {
        var retriever = CreateRetriever(new FakeEmbeddingClient(), new FakeVectorStore());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => retriever.SearchAsync(new KnowledgeSearchQuery("query", TopK: topK), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchAsync_EmbeddingClientThrows_PropagatesTypedException_NeverBecomesNoRelevantEvidence()
    {
        var embeddingClient = new FakeEmbeddingClient
        {
            ThrowOnNextCall = new KnowledgeEmbeddingException("boom", retryable: true),
        };
        var retriever = CreateRetriever(embeddingClient, new FakeVectorStore());

        await Assert.ThrowsAsync<KnowledgeEmbeddingException>(
            () => retriever.SearchAsync(new KnowledgeSearchQuery("query"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchAsync_VectorStoreThrows_PropagatesTypedException_NeverBecomesNoRelevantEvidence()
    {
        var vectorStore = new FakeVectorStore { ThrowOnQuery = new KnowledgeVectorStoreException("boom", retryable: true) };
        var retriever = CreateRetriever(new FakeEmbeddingClient(), vectorStore);

        await Assert.ThrowsAsync<KnowledgeVectorStoreException>(
            () => retriever.SearchAsync(new KnowledgeSearchQuery("query"), TestContext.Current.CancellationToken));
    }
}
