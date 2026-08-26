using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.Contracts.Mcp;

/// <summary>get_campaigns success data (P0-10). Campaigns is an empty array — never null — when the
/// customer exists but belongs to no campaign. Only campaigns this customer is eligible for are
/// ever returned (plan D10).</summary>
public sealed record GetCampaignsData(IReadOnlyList<CampaignDto> Campaigns);
