namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// Neutral abstraction over semantic retrieval against the RAG knowledge base
/// (docs/02_ARCHITECTURE.md §7). P0 implementation lives in CrmCopilot.McpServer and calls
/// Gemini embedding + Chroma. Consumed later by P0-04's search_product_knowledge tool and
/// P0-07's email-draft flow — not called by anything yet in P0-03.
/// </summary>
public interface IKnowledgeRetriever
{
    /// <summary>
    /// Throws <see cref="Exceptions.KnowledgeEmbeddingException"/> or
    /// <see cref="Exceptions.KnowledgeVectorStoreException"/> for embedding/vector-store
    /// failures — a Chroma/Gemini outage is never mapped to
    /// <see cref="KnowledgeSearchResult.NoRelevantEvidence"/> (AC-R06). Throws
    /// <see cref="ArgumentException"/>/<see cref="ArgumentOutOfRangeException"/> for an invalid
    /// query (blank/too-long text, out-of-range TopK) before any network call.
    /// </summary>
    Task<KnowledgeSearchResult> SearchAsync(KnowledgeSearchQuery query, CancellationToken cancellationToken);
}
