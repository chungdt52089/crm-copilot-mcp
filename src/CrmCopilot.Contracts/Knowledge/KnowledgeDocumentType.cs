namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// The two knowledge document types indexed in Chroma (docs/08_RAG_EMAIL_AND_PII_SPEC.md §2).
/// Customer/interaction data is never a member of this enum — that stays structured CRM
/// retrieval, never vector RAG (docs/02_ARCHITECTURE.md §7).
/// </summary>
public enum KnowledgeDocumentType
{
    Product,
    EmailTemplate,
}
