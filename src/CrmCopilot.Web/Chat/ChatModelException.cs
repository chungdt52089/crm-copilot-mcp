namespace CrmCopilot.Web.Chat;

/// <summary>
/// Raised by <see cref="IGeminiChatClient"/> for any Gemini generateContent failure. Message is
/// always a fixed, safe string — the caught SDK exception is attached only as InnerException,
/// never interpolated into Message (secret-hygiene rule established at the P0-03 review, reused
/// verbatim by CrmCopilot.McpServer.Knowledge.KnowledgeEmbeddingException).
/// </summary>
public sealed class ChatModelException(string message, bool retryable, Exception? inner = null)
    : Exception(message, inner)
{
    public bool Retryable { get; } = retryable;
}
