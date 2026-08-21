using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.Contracts.Mcp;

/// <summary>get_interactions success data (docs/07_MCP_TOOL_CONTRACTS.md §5). Interactions is an
/// empty array — never null — when the customer exists but has no interactions.</summary>
public sealed record GetInteractionsData(IReadOnlyList<InteractionDto> Interactions);
