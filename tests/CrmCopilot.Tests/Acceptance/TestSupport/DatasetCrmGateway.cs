using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.MockCrmApi.Data;
using CrmCopilot.MockCrmApi.Search;

namespace CrmCopilot.Tests.Acceptance.TestSupport;

/// <summary>
/// An <see cref="ICrmGateway"/> backed by the real checked-in dataset and the real P0-02 search
/// logic (<see cref="CustomerSearch"/>, <see cref="CrmDataset.GetInteractions"/>) — the same code
/// the Mock CRM API serves from, minus the HTTP hop.
///
/// Deliberately not FakeCrmGateway: that stub returns whatever a test assigns regardless of the
/// query, which would make T02/T03 (unique vs. duplicate name resolution) and T05 (newest-first
/// ordering) assert the test's own setup rather than the system's behavior. Here the ambiguity, the
/// ordering and the not-found path are all produced by production code paths.
///
/// The Last* properties are the no-bypass proof: they live only inside the in-memory McpServer's DI
/// container, so a Web-side assertion can only observe them via a genuine MCP client → server →
/// gateway round trip (same argument as ChatEndpointTests' class doc).
/// </summary>
internal sealed class DatasetCrmGateway : ICrmGateway
{
    private readonly CrmDataset _dataset = ScenarioDatasetSeed.Dataset;

    public CustomerLookupQuery? LastLookupQuery { get; private set; }
    public string? LastInteractionsCustomerId { get; private set; }
    public int? LastInteractionsLimit { get; private set; }

    public Task<CustomerLookupResult> FindCustomerAsync(CustomerLookupQuery query, CancellationToken cancellationToken)
    {
        LastLookupQuery = query;

        // CustomerSearch resolves an exact ID first, then a normalized name — so a single call
        // covers both the ById and ByQuery shapes exactly as the Mock CRM API endpoint does.
        var term = query.CustomerId ?? query.Query!;
        return Task.FromResult(CustomerSearch.Search(_dataset, term));
    }

    public Task<IReadOnlyList<InteractionDto>> GetInteractionsAsync(string customerId, int limit, CancellationToken cancellationToken)
    {
        LastInteractionsCustomerId = customerId;
        LastInteractionsLimit = limit;

        if (!_dataset.CustomerExists(customerId))
        {
            // Matches MockCrmGateway's mapping of the endpoint's 404 — never a silent empty list.
            throw new CrmNotFoundException(customerId, traceId: null);
        }

        return Task.FromResult(_dataset.GetInteractions(customerId, limit));
    }
}
