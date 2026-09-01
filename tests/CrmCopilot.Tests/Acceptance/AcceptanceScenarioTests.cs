namespace CrmCopilot.Tests.Acceptance;

/// <summary>
/// The deterministic (D) acceptance layer: exactly one fact, which drives
/// <see cref="AcceptanceScenarioRunner"/> over all eight docs/03 §6 scenarios in a fixed order,
/// writes the report, and only then asserts.
///
/// Deliberately not eight separate facts plus an aggregator: xUnit guarantees neither ordering nor
/// serialization between facts, so an aggregating fact could run first and read nothing. Driving the
/// sequence inside one fact makes the run order explicit and lets the report and the assertions read
/// the same result list.
///
/// This layer proves contract, schema, state and gate behavior against a real MCP protocol round
/// trip with faked Gemini/CRM/knowledge. It says nothing about the real Gemini or Chroma path —
/// that is <see cref="LiveAcceptanceScenarioTests"/>' job, and a pass here may never be reported as
/// live evidence (plan §2.3/§9.2).
/// </summary>
public class AcceptanceScenarioTests
{
    [Fact]
    public async Task EightScenarios_MeetLockedAcceptanceThreshold_AndProduceReport()
    {
        var runner = new AcceptanceScenarioRunner();

        var results = await runner.RunAsync(
            AcceptanceScenarioRunner.AllScenarios, TestContext.Current.CancellationToken);

        // Written before any assertion, so the report survives a failing run and can be read to
        // diagnose it.
        var reportPath = ScenarioReportWriter.Write(results, "acceptance-scenarios-offline.md");
        TestContext.Current.SendDiagnosticMessage($"Acceptance scenario report: {reportPath}");

        Assert.Equal(ScenarioReportWriter.TotalScenarioCount, results.Count);

        // (1) Infrastructure integrity, asserted FIRST and independently of the pass budget. An
        // "Error" scenario was never evaluated, so an X/8 computed alongside it would be meaningless
        // — this can never be traded against the one failure the ≥7/8 threshold allows.
        var errored = results.Where(result => result.Outcome == ScenarioOutcome.Error).ToList();
        Assert.True(
            errored.Count == 0,
            "Scenario(s) could not be evaluated (runner/harness failure, not a measured result): "
            + string.Join(" || ", errored.Select(result => $"{result.Id} — {result.FailureSummary}")));

        // (2) The locked acceptance threshold from docs/03_ACCEPTANCE_CRITERIA.md §6: 7/8 = 87.5%.
        // 8/8 is the quality target and is reported as such, but is deliberately NOT the pass
        // condition — asserting 8/8 here would silently raise a criterion the project has locked.
        var passedCount = results.Count(result => result.Outcome == ScenarioOutcome.Pass);
        var failures = results
            .Where(result => result.Outcome == ScenarioOutcome.Fail)
            .Select(result => $"{result.Id} — {result.FailureSummary}");

        Assert.True(
            passedCount >= ScenarioReportWriter.RequiredPassCount,
            $"ScenarioAccuracy {passedCount}/{ScenarioReportWriter.TotalScenarioCount} is below the locked "
            + $"{ScenarioReportWriter.RequiredPassCount}/{ScenarioReportWriter.TotalScenarioCount} threshold. "
            + $"Failed: {string.Join(" || ", failures)}");
    }
}
