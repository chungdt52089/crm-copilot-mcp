namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// The closed allowlist of <see cref="OpportunityDto.Status"/> values (plan Amendment A1). Owned
/// here, in Contracts, because three separate boundaries validate against it: the Mock CRM API
/// endpoint (400), the get_opportunities MCP tool (INVALID_ARGUMENT), and the dataset loader.
/// </summary>
public static class OpportunityStatuses
{
    public const string Open = "Open";
    public const string Won = "Won";
    public const string Lost = "Lost";
    public const string Closed = "Closed";

    public static readonly IReadOnlyList<string> All = [Open, Won, Lost, Closed];

    /// <summary>
    /// Case-insensitive on input, canonical on output — a model-driven tool call may well send
    /// "open", and rejecting that would be a usability failure, not a safety one. The normalized
    /// value is what every downstream comparison uses, so filtering stays ordinal and deterministic.
    /// </summary>
    public static bool TryNormalize(string? raw, out string canonical)
    {
        canonical = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        foreach (var candidate in All)
        {
            if (string.Equals(raw, candidate, StringComparison.OrdinalIgnoreCase))
            {
                canonical = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool IsValid(string? raw) => TryNormalize(raw, out _);
}
