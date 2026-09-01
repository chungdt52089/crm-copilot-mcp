using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.McpServer.Knowledge.Ingestion;

/// <summary>
/// One rendered knowledge document ready for embedding — fully-typed fields, not a loose
/// dictionary, so nothing in the ingestion pipeline needs untyped metadata handling.
/// ProductCode is set for Product, TemplateId for EmailTemplate; the other is null.
/// </summary>
internal sealed record KnowledgeSourceDocument(
    string SourceId,
    KnowledgeDocumentType DocumentType,
    string RenderedText,
    string? ProductCode,
    string? TemplateId,
    string Language,
    string Version);
