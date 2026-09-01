namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// The knowledge document types indexed in Chroma (docs/08_RAG_EMAIL_AND_PII_SPEC.md §2).
/// Customer/interaction data is never a member of this enum — that stays structured CRM
/// retrieval, never vector RAG (docs/02_ARCHITECTURE.md §7).
///
/// <see cref="CallScript"/> was added in P0-10. Because they share one collection with Product and
/// EmailTemplate, search_product_knowledge now filters explicitly rather than passing a null
/// (unfiltered) document-type filter — see KnowledgeTools (plan D7).
/// </summary>
public enum KnowledgeDocumentType
{
    Product,
    EmailTemplate,
    CallScript,
}
