namespace CrmCopilot.Contracts.Knowledge.Exceptions;

/// <summary>
/// Base type for typed failures raised by the RAG pipeline (embedding or vector-store calls).
/// Never raised for the ordinary Found/NoRelevantEvidence outcomes, which are represented as
/// ordinary return values, not exceptions. <see cref="Message"/> is always a fixed, static
/// description — the underlying SDK/HTTP exception is attached only via InnerException, never
/// string-interpolated into Message, so no caught exception's raw text (which can include
/// verbose upstream error bodies) reaches an ordinary "log ex.Message" call site.
/// </summary>
public abstract class KnowledgeGatewayException : Exception
{
    protected KnowledgeGatewayException(string message, bool retryable, Exception? inner = null)
        : base(message, inner)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}
