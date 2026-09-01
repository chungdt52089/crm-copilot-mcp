namespace CrmCopilot.Contracts.Crm.Exceptions;

/// <summary>
/// Base type for typed failures raised by an <see cref="ICrmGateway"/> implementation.
/// Never raised for the ordinary Found/NotFound/Ambiguous/empty-interactions outcomes, which
/// are represented as ordinary return values, not exceptions.
/// </summary>
public abstract class CrmGatewayException : Exception
{
    protected CrmGatewayException(string message, string? traceId, Exception? inner = null)
        : base(message, inner)
    {
        TraceId = traceId;
    }

    public string? TraceId { get; }
}
