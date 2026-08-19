namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// Synthetic customer profile (docs/06_DATA_AND_MOCK_API_SPEC.md §4). All checked-in records
/// have Synthetic = true.
/// </summary>
public sealed record CustomerDto(
    string Id,
    string FullName,
    string Email,
    string Phone,
    string AccountReference,
    string Segment,
    string City,
    string PreferredLanguage,
    string RelationshipManagerId,
    string Status,
    bool Synthetic,
    DateTime UpdatedAtUtc);
