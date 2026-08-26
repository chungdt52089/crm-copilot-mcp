using System.Globalization;
using System.Text;

namespace CrmCopilot.Tests.Acceptance;

/// <summary>
/// Renders a scenario run to markdown. Pure with respect to the results it is given — it never
/// re-evaluates anything, so the report is exactly the list the caller also asserts over
/// (plan §4.2 item 5).
/// </summary>
internal static class ScenarioReportWriter
{
    /// <summary>The locked target from docs/03_ACCEPTANCE_CRITERIA.md §6 (7/8 = 87.5%).</summary>
    public const int RequiredPassCount = 7;

    public const int TotalScenarioCount = 8;

    /// <summary>
    /// Writes under TestResults/, which .gitignore already excludes — the checked-in narrative lives
    /// in docs/14_ACCEPTANCE_SCENARIO_REPORT.md and is filled in from this output.
    /// </summary>
    public static string Write(IReadOnlyList<ScenarioResult> results, string fileName)
    {
        var markdown = Render(results);
        var directory = Path.Combine(FindRepositoryRoot(), "TestResults");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, markdown, Encoding.UTF8);
        return path;
    }

    public static string Render(IReadOnlyList<ScenarioResult> results)
    {
        var passed = results.Count(result => result.Outcome == ScenarioOutcome.Pass);
        var failed = results.Count(result => result.Outcome == ScenarioOutcome.Fail);
        var errored = results.Count(result => result.Outcome == ScenarioOutcome.Error);

        var builder = new StringBuilder();
        builder.AppendLine("# Acceptance scenario report");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Evidence classes present: {DescribeClasses(results)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- ScenarioAccuracy: **{passed}/{TotalScenarioCount}** (threshold {RequiredPassCount}/{TotalScenarioCount}, docs/03 §6)");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Failed: {failed} · Errored (not evaluated): {errored}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Quality target {TotalScenarioCount}/{TotalScenarioCount}: {DescribeQualityTarget(results)}");
        builder.AppendLine();
        builder.AppendLine("| ID | Scenario | Boundary | Evidence class | Outcome | Duration (ms) | Failed checks |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

        foreach (var result in results)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.Id} | {result.Title} | {result.Boundary} | {DescribeClass(result.Class)} | {DescribeOutcome(result.Outcome)} | {result.DurationMs} | {Cell(result.FailureSummary)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Checks per scenario");

        foreach (var result in results)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"### {result.Id} — {result.Title} ({DescribeOutcome(result.Outcome)})");
            builder.AppendLine();
            foreach (var check in result.Checks)
            {
                var mark = check.Passed ? "x" : " ";
                var detail = string.IsNullOrWhiteSpace(check.Detail) ? string.Empty : $" — {check.Detail}";
                builder.AppendLine(CultureInfo.InvariantCulture, $"- [{mark}] {check.Name}{detail}");
            }
        }

        return builder.ToString();
    }

    private static string DescribeQualityTarget(IReadOnlyList<ScenarioResult> results)
    {
        var notPassed = results.Where(result => result.Outcome != ScenarioOutcome.Pass).ToList();
        return notPassed.Count == 0
            ? "MET"
            : $"NOT MET — {string.Join(", ", notPassed.Select(result => $"{result.Id} ({DescribeOutcome(result.Outcome)})"))}";
    }

    private static string DescribeClasses(IReadOnlyList<ScenarioResult> results) =>
        results.Count == 0
            ? "(none)"
            : string.Join(", ", results.Select(result => DescribeClass(result.Class)).Distinct().Order(StringComparer.Ordinal));

    private static string DescribeClass(EvidenceClass evidenceClass) => evidenceClass switch
    {
        EvidenceClass.Deterministic => "D (offline)",
        EvidenceClass.Live => "L (live)",
        _ => evidenceClass.ToString(),
    };

    private static string DescribeOutcome(ScenarioOutcome outcome) => outcome switch
    {
        ScenarioOutcome.Pass => "PASS",
        ScenarioOutcome.Fail => "FAIL",
        ScenarioOutcome.Error => "ERROR (not evaluated)",
        _ => outcome.ToString(),
    };

    /// <summary>Escapes a pipe so a check detail can never break the markdown table it sits in.</summary>
    private static string Cell(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Replace("|", @"\|", StringComparison.Ordinal);

    /// <summary>
    /// Walks up from the test assembly's output directory to the directory holding CrmCopilot.slnx,
    /// so the report lands in the repository's own TestResults/ (already gitignored) rather than
    /// somewhere under bin/. Falls back to the base directory if the marker is never found.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CrmCopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
