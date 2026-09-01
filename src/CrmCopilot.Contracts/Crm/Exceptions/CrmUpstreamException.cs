namespace CrmCopilot.Contracts.Crm.Exceptions;

/// <summary>
/// Raised for any condition that is neither a clean domain outcome (found/not-found/ambiguous)
/// nor caller misuse: upstream 5xx, transport failures (timeout/connection error), unexpected
/// status codes, or a response body that fails to deserialize into the expected envelope shape.
/// </summary>
public sealed class CrmUpstreamException : CrmGatewayException
{
    public CrmUpstreamException(string message, bool retryable, string? traceId, Exception? inner = null)
        : base(message, traceId, inner)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}
