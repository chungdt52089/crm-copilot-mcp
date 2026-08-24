using CrmCopilot.McpServer.Email;

namespace CrmCopilot.Tests.Email.TestSupport;

/// <summary>
/// Deterministic offline stand-in for IEmailDraftGenerator. Unlike FakeCrmGateway/
/// FakeKnowledgeRetriever's single settable Result, this uses a Queue&lt;&gt; because generate_email's
/// retry loop must be able to return a *different* value across its two attempts within one call
/// (e.g. attempt 1 null, attempt 2 valid). CallCount proves "exactly N attempts, never more".
/// </summary>
internal sealed class FakeEmailDraftGenerator : IEmailDraftGenerator
{
    public Queue<RawEmailDraftModel?> Results { get; } = new();
    public Exception? ThrowOnGenerate { get; set; }
    public int CallCount { get; private set; }
    public EmailDraftPromptContext? LastContext { get; private set; }

    public Task<RawEmailDraftModel?> GenerateAsync(EmailDraftPromptContext context, CancellationToken cancellationToken)
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
