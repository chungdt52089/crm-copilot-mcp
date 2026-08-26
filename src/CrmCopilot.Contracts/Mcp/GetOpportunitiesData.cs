using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.Contracts.Mcp;

/// <summary>get_opportunities success data (P0-10). Opportunities is an empty array — never null —
/// when the customer exists but has no opportunity matching the requested status.</summary>
public sealed record GetOpportunitiesData(IReadOnlyList<OpportunityDto> Opportunities);
