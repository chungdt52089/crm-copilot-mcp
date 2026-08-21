using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.Tests.Knowledge.TestSupport;

/// <summary>
/// Deterministic offline stand-in for IKnowledgeRetriever, one layer above KnowledgeRetrieverTests
/// (which already covers KnowledgeRetriever against FakeEmbeddingClient/FakeVectorStore in P0-03)
/// — used by the P0-04 tool-layer tests.
/// </summary>
internal sealed class FakeKnowledgeRetriever : IKnowledgeRetriever
{
    public KnowledgeSearchResult? SearchResult { get; set; }
    public Exception? ThrowOnSearch { get; set; }
    public KnowledgeSearchQuery? LastQuery { get; private set; }

    public Task<KnowledgeSearchResult> SearchAsync(KnowledgeSearchQuery query, CancellationToken cancellationToken)
    {
        LastQuery = query;

        if (ThrowOnSearch is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(SearchResult ?? KnowledgeSearchResult.NoRelevantEvidence);
    }
}
