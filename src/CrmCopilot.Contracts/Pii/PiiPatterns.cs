using System.Text.RegularExpressions;

namespace CrmCopilot.Contracts.Pii;

/// <summary>
/// Shared PII-shape regexes — single source of truth reused by CrmCopilot.Web's InputGuard
/// (reject)/ConversationMessageSanitizer (redact) and CrmCopilot.McpServer's PiiMasker (P0-07
/// field-based + regex-fallback masking), so none of the three can ever drift apart on what
/// counts as an email/phone/account/secret-shaped value. Public and living in Contracts (not
/// CrmCopilot.Web.Chat, where it lived through P0-06) because Web and McpServer are separate
/// processes that do not reference each other but both already reference Contracts.
/// </summary>
public static class PiiPatterns
{
    public static readonly Regex Email = new(@"[^\s@]+@[^\s@]+\.[^\s@]+", RegexOptions.Compiled);

    public static readonly Regex Phone =
        new(@"(?<!\d)(\+84|0)[\s.-]?\d(?:[\s.-]?\d){8,9}(?!\d)", RegexOptions.Compiled);

    // 9+ consecutive digits — covers both a CCCD-shaped 12-digit number and this dataset's
    // accountReference (also a plain digit string, e.g. "000000000001" for CUS-0001). A
    // CUS-#### token (4 digits) never matches this.
    public static readonly Regex DigitRun = new(@"\d{9,}", RegexOptions.Compiled);

    // Long contiguous alphanumeric/-/_ run — real Gemini API keys (~39 chars, "AIza..." style) and
    // similar bearer tokens are unbroken strings of this shape. 24 is a conservative floor: long
    // enough that ordinary Vietnamese sentences/URLs in free text won't false-positive, short
    // enough to still catch realistic key/token lengths.
    public static readonly Regex SecretToken =
        new(@"(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{24,}(?![A-Za-z0-9_-])", RegexOptions.Compiled);
}
