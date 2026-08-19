namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// Synthetic customer interaction (docs/06_DATA_AND_MOCK_API_SPEC.md §4). CustomerId must
/// reference an existing CustomerDto.Id.
/// </summary>
public sealed record InteractionDto(
    string Id,
    string CustomerId,
    string Type,
    DateTime OccurredAtUtc,
    string Summary,
    string Outcome,
    string? NextAction,
    bool Synthetic);
