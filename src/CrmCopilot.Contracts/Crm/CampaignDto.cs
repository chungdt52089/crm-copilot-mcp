namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// Synthetic marketing campaign (P0-10).
///
/// <see cref="EligibleCustomerIds"/> is the deterministic campaign-to-customer relationship (plan
/// D10): membership is enumerated explicitly and never inferred from <see cref="TargetSegment"/>,
/// so "which campaigns is this customer in" has exactly one answer that does not depend on
/// segment-matching heuristics. TargetSegment is descriptive metadata only.
/// </summary>
public sealed record CampaignDto(
    string Id,
    string Name,
    string Objective,
    string TargetSegment,
    IReadOnlyList<string> ProductCodes,
    IReadOnlyList<string> EligibleCustomerIds,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    string Status,
    bool Synthetic);
