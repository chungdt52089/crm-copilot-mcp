namespace CrmCopilot.Tests.Acceptance;

/// <summary>
/// One recorded assertion inside a scenario. Scenarios record checks instead of throwing so a single
/// failure does not abort the remaining scenarios and leave a partial report.
/// <see cref="Detail"/> must stay free of stack traces and of raw PII — it is written verbatim into
/// the markdown report.
/// </summary>
internal sealed record CheckResult(string Name, bool Passed, string Detail);

/// <summary>
/// Accumulates <see cref="CheckResult"/>s for one scenario. Deliberately not an assertion library:
/// nothing here throws, because the runner needs every check evaluated and recorded even after an
/// earlier one has already failed.
/// </summary>
internal sealed class ScenarioChecklist
{
    private readonly List<CheckResult> _checks = [];

    public IReadOnlyList<CheckResult> Checks => _checks;

    public void Require(string name, bool passed, string detail = "") =>
        _checks.Add(new CheckResult(name, passed, detail));

    public void RequireEqual<T>(string name, T expected, T actual) =>
        _checks.Add(new CheckResult(
            name,
            EqualityComparer<T>.Default.Equals(expected, actual),
            $"expected={Render(expected)} actual={Render(actual)}"));

    public void RequireSequenceEqual(string name, IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var expectedList = expected.ToList();
        var actualList = actual.ToList();
        _checks.Add(new CheckResult(
            name,
            expectedList.SequenceEqual(actualList, StringComparer.Ordinal),
            $"expected=[{string.Join(", ", expectedList)}] actual=[{string.Join(", ", actualList)}]"));
    }

    public void RequireContains(string name, IEnumerable<string> haystack, string needle)
    {
        var list = haystack.ToList();
        _checks.Add(new CheckResult(
            name,
            list.Contains(needle, StringComparer.Ordinal),
            $"needle={needle} actual=[{string.Join(", ", list)}]"));
    }

    private static string Render<T>(T value) => value switch
    {
        null => "<null>",
        string text => text,
        _ => value.ToString() ?? "<null>",
    };
}
