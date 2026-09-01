namespace CrmCopilot.Contracts.Knowledge.Exceptions;

/// <summary>
/// Raised when a Chroma HTTP call fails: transport failure, an unexpected status code, a
/// malformed/unparseable response body, or an existing collection whose stored
/// embeddingModel/embeddingDimension/distanceMetric metadata does not match the configured
/// values (PD-009 — a model/dimension/metric change requires a new or re-indexed collection).
/// </summary>
public sealed class KnowledgeVectorStoreException : KnowledgeGatewayException
{
    public KnowledgeVectorStoreException(string message, bool retryable, Exception? inner = null)
        : base(message, retryable, inner)
    {
    }
}
