using System.Text.RegularExpressions;

namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// The shared shape rule for a product code (plan D18). Extracted from EmailTools, where the same
/// regex was previously private, because P0-10 added two more validation sites: CallScriptTools and
/// the Mock CRM API dataset loader.
///
/// It lives in Contracts rather than in CrmCopilot.McpServer (where the plan first sketched it)
/// because CrmCopilot.MockCrmApi validates campaign productCodes too and does not — and must not —
/// reference the MCP server project.
///
/// This is a FORMAT check only. It deliberately does not assert that the code exists in
/// data/knowledge/products.json: MockCrmApi has no copy of the knowledge dataset in its output
/// (only data/crm/*.json is copied into it), so cross-dataset referential integrity is enforced in
/// the test project, where both directories are present (plan D17).
/// </summary>
public static class ProductCodeFormat
{
    public const int MaxLength = 40;

    /// <summary>
    /// Derived from the 6 checked-in records in data/knowledge/products.json (docs/06 has no formal
    /// regex of its own): PRD- followed by 2-4 further hyphen-separated uppercase-alphanumeric
    /// segments, e.g. PRD-SAV-006M, PRD-CARD-CASHBACK-01. Same construction idiom as the other
    /// compiled patterns in this solution (PiiPatterns, EmailTools).
    /// </summary>
    private static readonly Regex Pattern = new(@"^PRD-[A-Z0-9]+(-[A-Z0-9]+){1,3}$", RegexOptions.Compiled);

    public static bool IsWellFormed(string? productCode) =>
        !string.IsNullOrWhiteSpace(productCode) &&
        productCode.Length <= MaxLength &&
        Pattern.IsMatch(productCode);
}
