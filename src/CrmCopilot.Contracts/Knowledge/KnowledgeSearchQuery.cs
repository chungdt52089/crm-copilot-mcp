namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// Retrieval request for <see cref="IKnowledgeRetriever"/>. <see cref="TopK"/> is the single
/// source of truth for how many results to request — there is deliberately no separate
/// server-side default, since a P0-04/P0-07 caller may reasonably want to override it per call.
/// </summary>
public sealed record KnowledgeSearchQuery(
    string QueryText,
    IReadOnlyList<KnowledgeDocumentType>? DocumentTypes = null,
    int TopK = 3);
