namespace CrmCopilot.Tests.Acceptance;

/// <summary>
/// Which evidence layer a result was produced by (plan §2.3). A layer can never stand in for
/// another: a <see cref="Deterministic"/> pass says nothing about the real Gemini/Chroma path, and
/// no automated layer says anything about the browser demo.
/// </summary>
internal enum EvidenceClass
{
    /// <summary>D — offline, real MCP protocol in-memory, faked Gemini/CRM/knowledge.</summary>
    Deterministic,

    /// <summary>L — real Gemini + real Chroma + real Mock CRM API. Opt-in.</summary>
    Live,
}

/// <summary>
/// The single source of truth for one scenario: <see cref="AcceptanceScenarioTests"/> writes the
/// report from the same list it then asserts over, so the report can never disagree with the verdict.
/// </summary>
internal sealed record ScenarioResult(
    ScenarioId Id,
    string Title,
    string Boundary,
    EvidenceClass Class,
    ScenarioOutcome Outcome,
    IReadOnlyList<CheckResult> Checks,
    long DurationMs)
{
    /// <summary>Failed check names + details, joined for an assertion message or a report cell.</summary>
    public string FailureSummary =>
        Checks.Count(check => !check.Passed) == 0
            ? string.Empty
            : string.Join(" | ", Checks.Where(check => !check.Passed).Select(check => $"{check.Name} ({check.Detail})"));

    public static ScenarioResult From(
        ScenarioId id, string title, string boundary, EvidenceClass evidenceClass,
        ScenarioChecklist checklist, long durationMs) =>
        new(
            id, title, boundary, evidenceClass,
            checklist.Checks.All(check => check.Passed) ? ScenarioOutcome.Pass : ScenarioOutcome.Fail,
            checklist.Checks, durationMs);

    /// <summary>
    /// A scenario that could not be evaluated. <paramref name="reason"/> must already be sanitized —
    /// see <see cref="AcceptanceScenarioRunner"/>'s boundary handler.
    /// </summary>
    public static ScenarioResult Errored(
        ScenarioId id, string title, string boundary, EvidenceClass evidenceClass,
        string reason, long durationMs) =>
        new(
            id, title, boundary, evidenceClass, ScenarioOutcome.Error,
            [new CheckResult("scenario evaluated", false, reason)], durationMs);
}
