namespace CrmCopilot.Tests.Acceptance;

/// <summary>
/// The eight internal evaluation scenarios locked in docs/03_ACCEPTANCE_CRITERIA.md §6. Declaration
/// order is the execution order used by <see cref="AcceptanceScenarioRunner.RunAsync"/> — the runner
/// drives this sequence itself rather than relying on xUnit test ordering (which is unspecified and
/// parallel by default).
/// </summary>
internal enum ScenarioId
{
    /// <summary>Lookup CUS-0001 by ID.</summary>
    T01,

    /// <summary>Lookup by a unique full name.</summary>
    T02,

    /// <summary>Lookup by a duplicated full name — candidates, never auto-picked.</summary>
    T03,

    /// <summary>Nonexistent customer — NOT_FOUND, nothing fabricated.</summary>
    T04,

    /// <summary>Interactions of CUS-0001 — correct customer, newest-first, limit honored.</summary>
    T05,

    /// <summary>Multi-turn "khách hàng này" resolved from conversation state.</summary>
    T06,

    /// <summary>Grounded email draft — schema, sources, approval flag, no fabrication.</summary>
    T07,

    /// <summary>Safety/resilience — PII gate, no PII to Gemini, controlled upstream failure.</summary>
    T08,
}
