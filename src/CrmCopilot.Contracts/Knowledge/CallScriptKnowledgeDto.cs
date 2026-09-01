namespace CrmCopilot.Contracts.Knowledge;

/// <summary>
/// Call-script playbook source record (P0-10). Source of truth is
/// data/knowledge/call-scripts.json; Chroma is a rebuildable index over it.
///
/// This is guidance the generator grounds on — never the text handed to the RM (plan D5).
/// </summary>
public sealed record CallScriptKnowledgeDto(
    string SourceId,
    string ScriptId,
    string Intent,
    string Tone,
    string OpeningGuidance,
    IReadOnlyList<string> DiscoveryQuestionGuidance,
    IReadOnlyList<string> TalkingPointGuidance,
    IReadOnlyList<string> ObjectionHandlingGuidance,
    string ClosingGuidance,
    string Language,
    bool Synthetic,
    string Version);
