namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// One retrieved knowledge chunk. <see cref="Distance"/> is the raw Chroma distance for the
/// configured metric (l2) — lower is closer. No invented "similarity score" formula on top of it.
/// </summary>
public sealed record KnowledgeMatch(
    string SourceId,
    KnowledgeDocumentType DocumentType,
    string Content,
    KnowledgeSourceMetadata Metadata,
    double Distance);
