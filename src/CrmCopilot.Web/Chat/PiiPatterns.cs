using System.Text.RegularExpressions;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Shared PII-shape regexes used by both <see cref="InputGuard"/> (reject) and
/// <see cref="ConversationMessageSanitizer"/> (redact) — a single source of truth so the two
/// consumers can never drift apart on what counts as an email/phone/account-shaped value.
/// </summary>
internal static class PiiPatterns
{
    public static readonly Regex Email = new(@"[^\s@]+@[^\s@]+\.[^\s@]+", RegexOptions.Compiled);

    public static readonly Regex Phone =
        new(@"(?<!\d)(\+84|0)[\s.-]?\d(?:[\s.-]?\d){8,9}(?!\d)", RegexOptions.Compiled);

    // 9+ consecutive digits — covers both a CCCD-shaped 12-digit number and this dataset's
    // accountReference (also a plain digit string, e.g. "000000000001" for CUS-0001). A
    // CUS-#### token (4 digits) never matches this.
    public static readonly Regex DigitRun = new(@"\d{9,}", RegexOptions.Compiled);
}
