namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// Email template knowledge source record (docs/06_DATA_AND_MOCK_API_SPEC.md §4 "Email template").
/// Source of truth is data/knowledge/email-templates.json; Chroma is a rebuildable index over it.
/// </summary>
public sealed record EmailTemplateKnowledgeDto(
    string SourceId,
    string TemplateId,
    string Intent,
    string Tone,
    string SubjectPattern,
    IReadOnlyList<string> BodyGuidance,
    string Language,
    bool Synthetic,
    string Version);
