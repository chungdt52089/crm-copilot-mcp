using CrmCopilot.McpServer.Email;

namespace CrmCopilot.Tests.Acceptance.TestSupport;

/// <summary>
/// Passes every call through to the real generator while recording the
/// <see cref="EmailDraftPromptContext"/> it was handed.
///
/// That context is the complete set of values <see cref="GeminiEmailDraftGenerator"/> builds its
/// prompt from, so scanning the captured contexts is a sound proof of what did — and did not —
/// reach Gemini, without having to intercept the SDK's own HTTP traffic or log the prompt (which
/// would itself be a PII sink).
/// </summary>
internal sealed class CapturingEmailDraftGenerator(IEmailDraftGenerator inner) : IEmailDraftGenerator
{
    private readonly List<EmailDraftPromptContext> _contexts = [];

    public IReadOnlyList<EmailDraftPromptContext> CapturedContexts => _contexts;

    public Task<RawEmailDraftModel?> GenerateAsync(EmailDraftPromptContext context, CancellationToken cancellationToken)
    {
        _contexts.Add(context);
        return inner.GenerateAsync(context, cancellationToken);
    }
}
