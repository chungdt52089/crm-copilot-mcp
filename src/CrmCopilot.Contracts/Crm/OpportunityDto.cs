namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// Synthetic sales opportunity (P0-10). CustomerId must reference an existing CustomerDto.Id and
/// ProductCode an existing product knowledge record.
///
/// <see cref="AmountVnd"/> reaches the Host/UI through the trusted local response path (the same
/// path CustomerDto already travels), but is never sent to Gemini: the call-script pipeline maps it
/// to a coarse band first (plan D12 / Amendment A2). Nothing else on this record is free text, so
/// there is no additional field for PiiMasker to scan.
/// </summary>
public sealed record OpportunityDto(
    string Id,
    string CustomerId,
    string ProductCode,
    string Stage,
    long AmountVnd,
    DateTime ExpectedCloseDateUtc,
    string Status,
    bool Synthetic);
