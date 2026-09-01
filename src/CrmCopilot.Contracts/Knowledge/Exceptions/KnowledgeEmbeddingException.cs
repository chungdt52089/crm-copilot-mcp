namespace CrmCopilot.Contracts.Knowledge.Exceptions;

/// <summary>
/// Raised when a Gemini embedContent call fails: transport failure, a Google.GenAI ClientError/
/// ServerError, an unexpected embedding dimension, or a malformed/empty response.
/// </summary>
public sealed class KnowledgeEmbeddingException : KnowledgeGatewayException
{
    public KnowledgeEmbeddingException(string message, bool retryable, Exception? inner = null)
        : base(message, retryable, inner)
    {
    }
}
