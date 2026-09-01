using CrmCopilot.Contracts.Crm.Exceptions;

namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// Neutral abstraction over CRM customer/interaction/opportunity/campaign lookups
/// (docs/06_DATA_AND_MOCK_API_SPEC.md §8). P0 implementation is MockCrmGateway, calling
/// CrmCopilot.MockCrmApi over HTTP. Adding a member to this interface is a breaking change for
/// every implementer — it is not a free extension point.
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

    /// <summary>
    /// P0-10. Returns an empty list only when the customer exists and has no opportunity matching
    /// <paramref name="status"/>.
    ///
    /// <paramref name="status"/> is optional; when supplied it must be one of
    /// <see cref="OpportunityStatuses.All"/> (case-insensitive) or
    /// <see cref="ArgumentException"/> is thrown. Filtering by status happens strictly BEFORE
    /// <paramref name="limit"/> is applied (plan Amendment A1) — otherwise a customer whose newest
    /// records are all Won would return nothing for a status=Open request.
    ///
    /// Ordering is ExpectedCloseDateUtc ascending, then Id ascending, so the "first Open
    /// opportunity" the call-script pipeline selects is deterministic.
    ///
    /// Throws <see cref="ArgumentOutOfRangeException"/> if <paramref name="limit"/> is outside
    /// 1-20. Throws <see cref="CrmNotFoundException"/> if the customer does not exist. Throws
    /// <see cref="CrmUpstreamException"/> for 5xx/transport/malformed-response conditions.
    /// </summary>
    Task<IReadOnlyList<OpportunityDto>> GetOpportunitiesAsync(
        string customerId, string? status, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// P0-10. Returns the campaigns whose <see cref="CampaignDto.EligibleCustomerIds"/> contains
    /// <paramref name="customerId"/> — never the full campaign list (plan D10). Returns an empty
    /// list only when the customer exists and belongs to no campaign.
    ///
    /// Ordering is StartDateUtc descending, then Id ascending. Throws
    /// <see cref="ArgumentOutOfRangeException"/> if <paramref name="limit"/> is outside 1-20.
    /// Throws <see cref="CrmNotFoundException"/> if the customer does not exist. Throws
    /// <see cref="CrmUpstreamException"/> for 5xx/transport/malformed-response conditions.
    /// </summary>
    Task<IReadOnlyList<CampaignDto>> GetCampaignsAsync(string customerId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// P0-14 (PD-023). Soft-deletes the customer — in the P0 implementation, in MockCrmApi's memory
    /// only, never on disk, so restarting that process restores the record. This is the one write
    /// member on this interface; every other member is a read.
    ///
    /// Throws <see cref="CrmNotFoundException"/> if the customer does not exist or was already
    /// deleted — the two are deliberately indistinguishable, matching what every read now reports
    /// about that id. Throws <see cref="CrmUpstreamException"/> for 5xx/transport conditions and for
    /// any unexpected success status, which signals upstream contract drift rather than a caller
    /// error.
    /// </summary>
    Task DeleteCustomerAsync(string customerId, CancellationToken cancellationToken);
}
