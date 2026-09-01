using System.Text.RegularExpressions;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Pii;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Pre-Gemini reject-gate on the raw chat message (plan D7). This is a REJECT gate, not a masker —
/// there is no placeholder/restore step in P0-05 (that is P0-07's subsystem). A message that
/// matches a rule is refused outright before any Gemini/MCP call; the RM is asked to rephrase.
///
/// Mechanism 1 (mechanical, category-specific): email / VN-style phone / long digit run (covers
/// both accountReference and a CCCD-shaped number — neither is a distinct field in this dataset;
/// see data/crm/customers.json, accountReference is itself a 12-digit string) / address heuristic.
/// Any match → PII_REJECTED.
///
/// Mechanism 2 (CRM-intent-without-ID): closes the raw-customer-name leak. If the message looks
/// customer/interaction-oriented (a CRM keyword, or a run of 2+ consecutive capitalized word
/// tokens — a crude Vietnamese full-name detector) but does not contain a valid CUS-#### token,
/// reject with CUSTOMER_ID_REQUIRED — UNLESS the signal is keyword-only (e.g. "khách hàng này")
/// and the caller supplies an active <c>currentCustomerId</c> from P0-06 conversation state, in
/// which case the message is allowed through as a resolvable follow-up (docs/02_ARCHITECTURE.md
/// §6: "khách hàng này" with no CurrentCustomerId must ask for clarification, not guess — with one
/// it should NOT ask). A capitalized-name-run signal is always rejected regardless of session
/// state — a literal name mention is a raw-name leak risk, not a pronoun follow-up. A generic
/// message with no CRM-intent signal at all (e.g. a product-knowledge question) is allowed through
/// unchanged. Both mechanisms are explicitly best-effort — over-rejection is the accepted failure
/// direction, not under-rejection.
/// </summary>
internal static class InputGuard
{
    private const string PiiRejectedMessage =
        "Vui lòng không nhập email, số điện thoại, số tài khoản/CCCD hoặc địa chỉ trực tiếp vào khung chat. Hãy dùng mã khách hàng (ví dụ CUS-0001).";

    private const string CustomerIdRequiredMessage =
        "Vui lòng cung cấp mã khách hàng (ví dụ CUS-0001).";

    private static readonly Regex AnyDigitPattern = new(@"\d", RegexOptions.Compiled);

    // Best-effort — no real fixture exists for a street address in this dataset's data model
    // (CustomerDto only has City); keyword-plus-digit co-occurrence is an interim approximation.
    private static readonly string[] AddressKeywords =
        ["đường", "phố", "ngõ", "số nhà", "phường", "xã", "quận", "huyện", "tỉnh", "thành phố"];

    private static readonly string[] CrmIntentKeywords =
        ["khách hàng", "customer", "tương tác", "interaction", "hồ sơ", "liên hệ", "lịch sử", "tài khoản"];

    private static readonly Regex CustomerIdPattern = new(@"\bCUS-\d{4}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A run of 2+ consecutive Titlecase word tokens — a crude Vietnamese full-name detector
    // ("Nguyễn Minh Anh"-shaped). Unicode-aware: \p{Lu}/\p{Ll} match Vietnamese diacritic
    // letters under their Unicode general category, not just ASCII A-Z/a-z.
    private static readonly Regex CapitalizedRunPattern =
        new(@"\p{Lu}\p{Ll}*(?:[ \t]+\p{Lu}\p{Ll}*){1,}", RegexOptions.Compiled);

    private const int MaxMessageLength = 2000;

    public static InputGuardResult Validate(string message, string? currentCustomerId = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return InputGuardResult.Reject(ChatTurnErrorCode.InvalidArgument, "Tin nhắn không được để trống.");
        }

        if (message.Length > MaxMessageLength)
        {
            return InputGuardResult.Reject(ChatTurnErrorCode.InvalidArgument, $"Tin nhắn vượt quá {MaxMessageLength} ký tự.");
        }

        if (PiiPatterns.Email.IsMatch(message) || PiiPatterns.Phone.IsMatch(message) ||
            PiiPatterns.DigitRun.IsMatch(message) || LooksLikeAddress(message))
        {
            return InputGuardResult.Reject(ChatTurnErrorCode.PiiRejected, PiiRejectedMessage);
        }

        // P0-10, browser-verified. A malformed customer id must die here, before Gemini sees it.
        // Downstream every path is wrong in a different way: the model silently substitutes the
        // session's customer (looks like success, wrong customer), or forwards the typo as a lookup
        // that answers NOT_FOUND (implies the id was well-formed and merely absent), or treats it as
        // a name query which then collides with the customerId the Host injects from session state
        // (surfaces an internal validator message). Checked before the valid-id branch so a message
        // mixing a good and a bad id is refused rather than half-honoured.
        if (CustomerIdFormat.TryFindMalformedToken(message, out _))
        {
            return InputGuardResult.Reject(ChatTurnErrorCode.CustomerIdInvalid, CustomerIdFormat.InvalidMessage);
        }

        var containsValidCustomerId = CustomerIdPattern.IsMatch(message);
        if (!containsValidCustomerId)
        {
            // A literal name mention is always rejected, regardless of session state — this is a
            // raw-name leak risk, not a pronoun follow-up like "khách hàng này".
            if (CapitalizedRunPattern.IsMatch(message))
            {
                return InputGuardResult.Reject(ChatTurnErrorCode.CustomerIdRequired, CustomerIdRequiredMessage);
            }

            if (ContainsCrmIntentKeyword(message))
            {
                // A keyword-only follow-up ("khách hàng này", "tương tác gần đây") is resolvable
                // against P0-06 conversation state when a customer is already active in this
                // session; the Host substitutes the ID downstream. With no active customer, ask
                // for clarification instead of guessing (docs/02_ARCHITECTURE.md §6).
                if (currentCustomerId is not null)
                {
                    return InputGuardResult.Ok();
                }

                return InputGuardResult.Reject(ChatTurnErrorCode.CustomerIdRequired, CustomerIdRequiredMessage);
            }
        }

        return InputGuardResult.Ok();
    }

    private static bool ContainsCrmIntentKeyword(string message) =>
        CrmIntentKeywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeAddress(string message) =>
        AddressKeywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase)) &&
        AnyDigitPattern.IsMatch(message);
}

internal sealed record InputGuardResult(bool IsAllowed, string? ErrorCode, string? ErrorMessage)
{
    public static InputGuardResult Ok() => new(true, null, null);

    public static InputGuardResult Reject(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}
