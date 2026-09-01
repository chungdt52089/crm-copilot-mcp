using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace CrmCopilot.Contracts.Api;

/// <summary>
/// Shared System.Text.Json configuration for CRM wire/data formats (camelCase field names
/// matching docs/06_DATA_AND_MOCK_API_SPEC.md). Used by the dataset generator/loader and by
/// MockCrmGateway when deserializing HTTP responses, since neither runs through ASP.NET Core's
/// own default web JSON options. The encoder allows Vietnamese diacritics through unescaped
/// (readable JSON/diffs) while still escaping HTML-sensitive characters (&lt;, &gt;, &amp;, ')
/// the same as the default encoder.
/// </summary>
public static class CrmJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Create(
            UnicodeRanges.BasicLatin,
            UnicodeRanges.Latin1Supplement,
            UnicodeRanges.LatinExtendedA,
            UnicodeRanges.LatinExtendedB,
            UnicodeRanges.LatinExtendedAdditional),
    };

    public static readonly JsonSerializerOptions Indented = new(Default)
    {
        WriteIndented = true,
    };
}
