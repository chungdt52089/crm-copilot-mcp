using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.Tests.Crm.TestSupport;

/// <summary>
/// Deterministic offline stand-in for ICrmGateway, one layer above MockCrmGatewayTests (which
/// already covers the HTTP-mapping layer in P0-02) — used by the P0-04 tool-layer tests.
/// </summary>
internal sealed class FakeCrmGateway : ICrmGateway
{
    public CustomerLookupResult? FindCustomerResult { get; set; }
    public Exception? ThrowOnFindCustomer { get; set; }
    public IReadOnlyList<InteractionDto>? InteractionsResult { get; set; }
    public Exception? ThrowOnGetInteractions { get; set; }
    public CustomerLookupQuery? LastLookupQuery { get; private set; }
    public string? LastInteractionsCustomerId { get; private set; }
    public int? LastInteractionsLimit { get; private set; }

    public Task<CustomerLookupResult> FindCustomerAsync(CustomerLookupQuery query, CancellationToken cancellationToken)
    {
        LastLookupQuery = query;

        if (ThrowOnFindCustomer is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(FindCustomerResult ?? CustomerLookupResult.NotFound);
    }

    public Task<IReadOnlyList<InteractionDto>> GetInteractionsAsync(string customerId, int limit, CancellationToken cancellationToken)
    {
        LastInteractionsCustomerId = customerId;
        LastInteractionsLimit = limit;

        if (ThrowOnGetInteractions is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(InteractionsResult ?? []);
    }
}
