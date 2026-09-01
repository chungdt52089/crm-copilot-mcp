using CrmCopilot.McpServer.CallScript;

namespace CrmCopilot.Tests.CallScript.TestSupport;

/// <summary>
/// Stand-in for the deterministic periodic-care template pin. Empty by default so a test must opt
/// in to the pin being available, which keeps the retrieval-fallback branch reachable.
/// </summary>
internal sealed class FakeCallScriptTemplateCatalog : ICallScriptTemplateCatalog
{
    public Dictionary<string, CallScriptEvidence> Entries { get; } = new(StringComparer.Ordinal);
    public string? LastRequestedScriptId { get; private set; }

    public CallScriptEvidence? FindByScriptId(string scriptId)
    {
        LastRequestedScriptId = scriptId;
        return Entries.GetValueOrDefault(scriptId);
    }
}
