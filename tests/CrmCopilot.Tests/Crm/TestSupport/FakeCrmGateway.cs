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
    public IReadOnlyList<OpportunityDto>? OpportunitiesResult { get; set; }
    public Exception? ThrowOnGetOpportunities { get; set; }
    public IReadOnlyList<CampaignDto>? CampaignsResult { get; set; }
    public Exception? ThrowOnGetCampaigns { get; set; }
    public CustomerLookupQuery? LastLookupQuery { get; private set; }
    public string? LastInteractionsCustomerId { get; private set; }
    public int? LastInteractionsLimit { get; private set; }
    public string? LastOpportunitiesCustomerId { get; private set; }
    public string? LastOpportunitiesStatus { get; private set; }
    public int? LastOpportunitiesLimit { get; private set; }
    public string? LastCampaignsCustomerId { get; private set; }
    public int? LastCampaignsLimit { get; private set; }

    /// <summary>
    /// Clears the captured Last* values so a multi-turn test can assert "no further call happened"
    /// after a setup turn that legitimately made one.
    /// </summary>
    public void ResetCallTracking()
    {
        LastLookupQuery = null;
        LastInteractionsCustomerId = null;
        LastInteractionsLimit = null;
        LastOpportunitiesCustomerId = null;
        LastOpportunitiesStatus = null;
        LastOpportunitiesLimit = null;
        LastCampaignsCustomerId = null;
        LastCampaignsLimit = null;
    }

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

    public Task<IReadOnlyList<OpportunityDto>> GetOpportunitiesAsync(
        string customerId, string? status, int limit, CancellationToken cancellationToken)
    {
        LastOpportunitiesCustomerId = customerId;
        LastOpportunitiesStatus = status;
        LastOpportunitiesLimit = limit;

        if (ThrowOnGetOpportunities is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(OpportunitiesResult ?? []);
    }

    public Task<IReadOnlyList<CampaignDto>> GetCampaignsAsync(string customerId, int limit, CancellationToken cancellationToken)
    {
        LastCampaignsCustomerId = customerId;
        LastCampaignsLimit = limit;

        if (ThrowOnGetCampaigns is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(CampaignsResult ?? []);
    }
}
