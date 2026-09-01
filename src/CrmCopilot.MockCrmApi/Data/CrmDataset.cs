using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.MockCrmApi.Data;

/// <summary>
/// In-memory, read-only view over the loaded synthetic Customer/Interaction/Opportunity/Campaign
/// dataset. One instance is registered as a singleton for the process lifetime — P0 does not
/// persist or mutate CRM data.
/// </summary>
internal sealed class CrmDataset
{
    private readonly Dictionary<string, CustomerDto> _customersById;
    private readonly ILookup<string, InteractionDto> _interactionsByCustomerId;
    private readonly ILookup<string, OpportunityDto> _opportunitiesByCustomerId;
    private readonly ILookup<string, CampaignDto> _campaignsByEligibleCustomerId;

    public CrmDataset(
        IReadOnlyList<CustomerDto> customers,
        IReadOnlyList<InteractionDto> interactions,
        IReadOnlyList<OpportunityDto> opportunities,
        IReadOnlyList<CampaignDto> campaigns)
    {
        Customers = customers;
        Interactions = interactions;
        Opportunities = opportunities;
        Campaigns = campaigns;
        _customersById = customers.ToDictionary(customer => customer.Id);
        _interactionsByCustomerId = interactions.ToLookup(interaction => interaction.CustomerId);
        _opportunitiesByCustomerId = opportunities.ToLookup(opportunity => opportunity.CustomerId);

        // Flattened once here rather than scanned per request: a campaign lists the customers it is
        // for, so the customer-to-campaign direction the tool actually queries needs the inverse.
        _campaignsByEligibleCustomerId = campaigns
            .SelectMany(campaign => campaign.EligibleCustomerIds.Select(customerId => (customerId, campaign)))
            .ToLookup(pair => pair.customerId, pair => pair.campaign);
    }

    public IReadOnlyList<CustomerDto> Customers { get; }

    public IReadOnlyList<InteractionDto> Interactions { get; }

    public IReadOnlyList<OpportunityDto> Opportunities { get; }

    public IReadOnlyList<CampaignDto> Campaigns { get; }

    public CustomerDto? FindById(string customerId) => _customersById.GetValueOrDefault(customerId);

    public bool CustomerExists(string customerId) => _customersById.ContainsKey(customerId);

    public IReadOnlyList<InteractionDto> GetInteractions(string customerId, int limit) =>
        _interactionsByCustomerId[customerId]
            .OrderByDescending(interaction => interaction.OccurredAtUtc)
            .Take(limit)
            .ToList();

    /// <summary>
    /// The single place the P0-10 opportunity contract is enforced (plan Amendment A1). The status
    /// filter is applied BEFORE Take(limit): applying the limit first would let a customer whose
    /// earliest-closing records are all Won swallow the whole page and return an empty result for a
    /// status=Open request. Ordering is ExpectedCloseDateUtc ascending with Id as the tie-break, so
    /// "the first Open opportunity" is a deterministic record, not whichever one enumerated first.
    ///
    /// <paramref name="status"/> is expected already normalized by the caller
    /// (<see cref="OpportunityStatuses.TryNormalize"/>); comparison here is ordinal.
    /// </summary>
    public IReadOnlyList<OpportunityDto> GetOpportunities(string customerId, string? status, int limit)
    {
        IEnumerable<OpportunityDto> query = _opportunitiesByCustomerId[customerId];

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(opportunity => string.Equals(opportunity.Status, status, StringComparison.Ordinal));
        }

        return query
            .OrderBy(opportunity => opportunity.ExpectedCloseDateUtc)
            .ThenBy(opportunity => opportunity.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Campaigns this customer is explicitly eligible for — never the full campaign list (plan
    /// D10). Newest campaign first, Id as tie-break.
    /// </summary>
    public IReadOnlyList<CampaignDto> GetCampaigns(string customerId, int limit) =>
        _campaignsByEligibleCustomerId[customerId]
            .OrderByDescending(campaign => campaign.StartDateUtc)
            .ThenBy(campaign => campaign.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
}
