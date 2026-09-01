namespace CrmCopilot.Contracts.Knowledge;

public enum KnowledgeSearchStatus
{
    Found,
    NoRelevantEvidence,
}

/// <summary>
/// <see cref="KnowledgeSearchStatus.NoRelevantEvidence"/> means the query itself succeeded but
/// nothing cleared the configured distance threshold — it is never used to paper over a Chroma
/// or Gemini failure. Those surface as <see cref="Exceptions.KnowledgeVectorStoreException"/> /
/// <see cref="Exceptions.KnowledgeEmbeddingException"/> instead (AC-R06).
/// </summary>
public sealed record KnowledgeSearchResult(KnowledgeSearchStatus Status, IReadOnlyList<KnowledgeMatch> Matches)
{
    public static KnowledgeSearchResult Found(IReadOnlyList<KnowledgeMatch> matches) => new(KnowledgeSearchStatus.Found, matches);

    public static readonly KnowledgeSearchResult NoRelevantEvidence = new(KnowledgeSearchStatus.NoRelevantEvidence, []);
}
