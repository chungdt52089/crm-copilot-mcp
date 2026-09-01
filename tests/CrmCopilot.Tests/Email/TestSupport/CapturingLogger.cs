using Microsoft.Extensions.Logging;

namespace CrmCopilot.Tests.Email.TestSupport;

/// <summary>
/// Captures every Log* call's rendered message AND raw structured state separately (P0-07
/// amendment ✏️26: "a forbidden substring could hide in a structured property that never appears
/// in the rendered message text"). Never writes anywhere itself — pure in-memory capture for
/// assertions.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var values = new List<KeyValuePair<string, object?>>();
        if (state is IReadOnlyList<KeyValuePair<string, object>> kvps)
        {
            values.AddRange(kvps.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value)));
        }

        Entries.Add(new LogEntry(logLevel, message, values, exception));
    }

    /// <summary>Every rendered message, every individual structured-state value, AND the captured
    /// exception's own ToString() (F2 fix), concatenated — the single haystack a log-hygiene test
    /// should scan for forbidden substrings. Including Exception.ToString() here is defense-in-depth
    /// only: production code is expected to never pass a non-null exception to ILogger at all (see
    /// EmailToolsLogHygieneTests' separate assertion that every captured Exception is null) — this
    /// scan still catches a leak here even if that production rule were ever violated.</summary>
    public string AllCapturedText() => string.Join(
        "\n",
        Entries.Select(entry =>
            entry.Message + " " + string.Join(" ", entry.StateValues.Select(kv => $"{kv.Key}={kv.Value}")) + " " + entry.Exception?.ToString()));
}

internal sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> StateValues, Exception? Exception);
