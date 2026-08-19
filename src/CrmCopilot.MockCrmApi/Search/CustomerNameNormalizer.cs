namespace CrmCopilot.MockCrmApi.Search;

/// <summary>
/// Single normalization rule (trim, collapse whitespace, case-insensitive) shared by the
/// dataset generator (to guarantee only the deliberate duplicate pair collides) and
/// CustomerSearch (to match search queries) — kept in one place so they cannot drift apart.
/// </summary>
internal static class CustomerNameNormalizer
{
    public static string Normalize(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
