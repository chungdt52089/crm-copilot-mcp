namespace CrmCopilot.Contracts.Crm.Exceptions;

/// <summary>
/// Raised by <see cref="ICrmGateway.GetInteractionsAsync"/> when the customer does not exist
/// (upstream 404). Never returned silently as an empty interaction list.
/// </summary>
public sealed class CrmNotFoundException : CrmGatewayException
{
    public CrmNotFoundException(string customerId, string? traceId)
        : base($"Customer '{customerId}' was not found.", traceId)
    {
        CustomerId = customerId;
    }

    public string CustomerId { get; }
}
