using CrmCopilot.McpServer.CallScript;

namespace CrmCopilot.Tests.CallScript.TestSupport;

/// <summary>
/// Deterministic offline stand-in for ICallScriptGenerator. Uses a Queue rather than a single
/// settable Result because the retry loop must be able to return a different value across its two
/// attempts within one call. CallCount is what proves "exactly N attempts, never more" — and, for
/// the not-found gates, that the model was never reached at all.
/// </summary>
internal sealed class FakeCallScriptGenerator : ICallScriptGenerator
{
    public Queue<RawCallScriptModel?> Results { get; } = new();
    public Exception? ThrowOnGenerate { get; set; }
    public int CallCount { get; private set; }
    public CallScriptPromptContext? LastContext { get; private set; }

    public Task<RawCallScriptModel?> GenerateAsync(CallScriptPromptContext context, CancellationToken cancellationToken)
    {
        CallCount++;
        LastContext = context;

        if (ThrowOnGenerate is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : null);
    }
}
