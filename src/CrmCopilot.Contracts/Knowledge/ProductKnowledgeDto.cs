namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// Product knowledge source record (docs/06_DATA_AND_MOCK_API_SPEC.md §4 "Product knowledge").
/// Source of truth is data/knowledge/products.json; Chroma is a rebuildable index over it.
/// </summary>
public sealed record ProductKnowledgeDto(
    string SourceId,
    string ProductCode,
    string Name,
    string Category,
    string Summary,
    IReadOnlyList<string> Eligibility,
    IReadOnlyList<string> Benefits,
    IReadOnlyList<string> Constraints,
    string Language,
    bool Synthetic,
    string Version);
