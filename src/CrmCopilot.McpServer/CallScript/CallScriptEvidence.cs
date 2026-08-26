using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// One call-script playbook as fed into the generation prompt.
///
/// A dedicated type rather than <see cref="KnowledgeMatch"/> because call-script evidence reaches
/// the prompt through two different routes: normal semantic retrieval, and the deterministic pin
/// used by the periodic-care fallback (plan Amendment A6 step 4). Reusing KnowledgeMatch for the
/// pinned route would mean fabricating a KnowledgeSourceMetadata full of embedding model/dimension/
/// distance values that were never computed for it — inventing provenance the pin does not have.
/// This shape carries only what the prompt actually needs.
/// </summary>
internal sealed record CallScriptEvidence(string SourceId, string? ScriptId, string Content)
{
    public static CallScriptEvidence FromMatch(KnowledgeMatch match) =>
        new(match.SourceId, match.Metadata.TemplateId, match.Content);
}
