namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// Fully-typed shape of the metadata stored alongside every vector in the Chroma collection
/// (docs/08_RAG_EMAIL_AND_PII_SPEC.md §2/AC-R03), and returned with every retrieval match.
/// Deliberately not a loose Dictionary&lt;string, object&gt; — System.Text.Json deserializes
/// object-typed dictionary values as boxed JsonElement, which breaks naive equality checks
/// (e.g. comparing <see cref="Fingerprint"/> for the ingestion idempotency skip-check). See
/// CrmCopilot.McpServer.Knowledge.ChromaMetadataParser for the read-side JsonElement handling.
/// </summary>
public sealed record KnowledgeSourceMetadata(
    string SourceId,
    KnowledgeDocumentType DocumentType,
    string? ProductCode,
    string? TemplateId,
    string Language,
    string Version,
    string EmbeddingModel,
    int EmbeddingDimension,
    string DistanceMetric,
    bool Normalized,
    string Fingerprint);
