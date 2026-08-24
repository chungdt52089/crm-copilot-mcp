using CrmCopilot.Contracts.Pii;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Redacts a message before it is written into P0-06 conversation state
/// (docs/02_ARCHITECTURE.md §6: "không giữ raw phone/email/account"). This is deliberately
/// decoupled from <see cref="InputGuard"/>'s reject-gate — "the guard already rejected the bad
/// ones" is not the same guarantee as "safe to persist" — and reuses the guard's own
/// <see cref="PiiPatterns"/> so the two never define the same category two different ways.
/// A <c>CUS-####</c> token is never touched: it is 4 digits, below <see cref="PiiPatterns.DigitRun"/>'s
/// 9-digit threshold, and matches neither the email nor phone pattern. Only the three mechanical
/// categories are redacted here — the address heuristic is reject-only upstream, so an
/// address-shaped message never reaches this method at all.
/// </summary>
internal static class ConversationMessageSanitizer
{
    private const string EmailPlaceholder = "[redacted-email]";
    private const string PhonePlaceholder = "[redacted-phone]";
    private const string AccountPlaceholder = "[redacted-account]";

    public static string Sanitize(string message)
    {
        // Order matters: a phone number is digit-heavy enough to also match DigitRun, so Phone
        // must be replaced first or DigitRun would double-redact it as "[redacted-account]".
        var result = PiiPatterns.Email.Replace(message, EmailPlaceholder);
        result = PiiPatterns.Phone.Replace(result, PhonePlaceholder);
        result = PiiPatterns.DigitRun.Replace(result, AccountPlaceholder);
        return result;
    }
}
