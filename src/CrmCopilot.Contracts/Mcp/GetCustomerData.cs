using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.Contracts.Mcp;

/// <summary>get_customer success data (docs/07_MCP_TOOL_CONTRACTS.md §4).</summary>
public sealed record GetCustomerFoundData(CustomerDto Customer);

/// <summary>get_customer ambiguous-match data — same candidate shape as the P0-02 409
/// AMBIGUOUS_MATCH response (docs/06_DATA_AND_MOCK_API_SPEC.md §7).</summary>
public sealed record GetCustomerAmbiguousData(IReadOnlyList<CustomerCandidateDto> Candidates);
