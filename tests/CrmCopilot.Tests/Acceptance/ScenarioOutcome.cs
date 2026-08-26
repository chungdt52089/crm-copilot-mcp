namespace CrmCopilot.Tests.Acceptance;

/// <summary>
/// Outcome of one scenario. The <see cref="Fail"/>/<see cref="Error"/> split is what keeps the
/// docs/03 §6 "≥7/8" budget honest: only <see cref="Fail"/> is a *measured* negative result that the
/// budget may absorb. <see cref="Error"/> means the scenario was never actually evaluated, so it can
/// never be traded against the budget — <see cref="AcceptanceScenarioTests"/> asserts zero
/// <see cref="Error"/> results before it looks at the pass count at all.
/// </summary>
internal enum ScenarioOutcome
{
    /// <summary>Evaluated; every check passed.</summary>
    Pass,

    /// <summary>Evaluated; at least one check failed. Counts against ScenarioAccuracy.</summary>
    Fail,

    /// <summary>
    /// Not evaluated — harness/host/transport failure or an unexpected exception at the runner
    /// boundary. Never downgrade this to <see cref="Fail"/> to fit inside the 1-failure budget.
    /// </summary>
    Error,
}
