using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Mcp;

namespace CrmCopilot.Contracts.Chat;

/// <summary>
/// Structured evidence accumulated Host-side during a chat turn (plan D1). Populated directly from
/// MCP tool results — never round-tripped through Gemini. Only the fields the turn actually
/// touched are non-null. Reuses <see cref="Mcp.KnowledgeMatchDto"/> (the P0-04 wire-safe shape)
/// rather than <see cref="Knowledge.KnowledgeMatch"/> (which carries the heavier
/// <see cref="Knowledge.KnowledgeSourceMetadata"/>). <see cref="EmailDraft"/> (P0-08) and
/// <see cref="CallScript"/> (P0-10) reuse their Mcp DTOs for the same reason.
/// </summary>
public sealed record ChatResponseData(
    CustomerDto? Customer,
    IReadOnlyList<CustomerCandidateDto>? CustomerCandidates,
    IReadOnlyList<InteractionDto>? Interactions,
    IReadOnlyList<KnowledgeMatchDto>? KnowledgeMatches,
    EmailDraftDto? EmailDraft,
    IReadOnlyList<OpportunityDto>? Opportunities = null,
    IReadOnlyList<CampaignDto>? Campaigns = null,
    CallScriptDraftDto? CallScript = null);
