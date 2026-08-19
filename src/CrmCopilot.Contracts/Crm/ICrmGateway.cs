using CrmCopilot.Contracts.Crm.Exceptions;

namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// Neutral abstraction over CRM customer/interaction lookups (docs/06_DATA_AND_MOCK_API_SPEC.md
/// §8). P0 implementation is MockCrmGateway, calling CrmCopilot.MockCrmApi over HTTP. Adding a
/// member to this interface is a breaking change for every implementer — it is not a free
/// extension point.
/// </summary>
public interface ICrmGateway
{
    /// <summary>
    /// Throws <see cref="CrmUpstreamException"/> for 5xx/transport/malformed-response
    /// conditions. Never throws for a clean not-found or ambiguous result — those are
    /// represented in <see cref="CustomerLookupResult.Status"/>.
    /// </summary>
    Task<CustomerLookupResult> FindCustomerAsync(CustomerLookupQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Returns an empty list only when the customer exists and has no interactions.
    /// Throws <see cref="ArgumentOutOfRangeException"/> if <paramref name="limit"/> is outside
    /// 1-20. Throws <see cref="CrmNotFoundException"/> if the customer does not exist — never
    /// returns an empty list for that case. Throws <see cref="CrmUpstreamException"/> for
    /// 5xx/transport/malformed-response conditions.
    /// </summary>
    Task<IReadOnlyList<InteractionDto>> GetInteractionsAsync(string customerId, int limit, CancellationToken cancellationToken);
}
