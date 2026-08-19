namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// Minimal candidate shape returned on an ambiguous name search
/// (docs/06_DATA_AND_MOCK_API_SPEC.md §7) — never the full CustomerDto.
/// </summary>
public sealed record CustomerCandidateDto(string Id, string FullName, string Segment, string City);
