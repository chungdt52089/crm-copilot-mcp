namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// Direct, non-semantic lookup of a call-script playbook by its scriptId.
///
/// This exists for exactly one path: the periodic-care fallback, where the Product Owner required
/// the short-sentence demo to be deterministic (plan Amendment A6 step 4). Semantic ranking cannot
/// guarantee which template comes back, so that one path pins its template by id instead. Every
/// other path — including all product evidence on this same path — still goes through real
/// retrieval.
/// </summary>
internal interface ICallScriptTemplateCatalog
{
    /// <summary>Returns null when no playbook with that scriptId is present in the dataset.</summary>
    CallScriptEvidence? FindByScriptId(string scriptId);
}
