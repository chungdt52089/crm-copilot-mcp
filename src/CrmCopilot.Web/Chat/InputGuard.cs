using System.Text.RegularExpressions;
using CrmCopilot.Contracts.Chat;

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
/// reject with CUSTOMER_ID_REQUIRED. A generic message with no CRM-intent signal at all (e.g. a
/// product-knowledge question) is allowed through unchanged. Both mechanisms are explicitly
/// best-effort — over-rejection is the accepted failure direction, not under-rejection.
/// </summary>
internal static class InputGuard
{
    private const string PiiRejectedMessage =
        "Vui lòng không nhập email, số điện thoại, số tài khoản/CCCD hoặc địa chỉ trực tiếp vào khung chat. Hãy dùng mã khách hàng (ví dụ CUS-0001).";

    private const string CustomerIdRequiredMessage =
        "Vui lòng cung cấp mã khách hàng (ví dụ CUS-0001) thay vì tên hoặc mô tả khách hàng.";

    private static readonly Regex EmailPattern = new(@"[^\s@]+@[^\s@]+\.[^\s@]+", RegexOptions.Compiled);

    private static readonly Regex PhonePattern =
        new(@"(?<!\d)(\+84|0)[\s.-]?\d(?:[\s.-]?\d){8,9}(?!\d)", RegexOptions.Compiled);

    // 9+ consecutive digits — covers both a CCCD-shaped 12-digit number and this dataset's
    // accountReference (also a plain digit string, e.g. "000000000001" for CUS-0001).
    private static readonly Regex DigitRunPattern = new(@"\d{9,}", RegexOptions.Compiled);

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

    public static InputGuardResult Validate(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return InputGuardResult.Reject(ChatTurnErrorCode.InvalidArgument, "Tin nhắn không được để trống.");
        }

        if (message.Length > MaxMessageLength)
        {
            return InputGuardResult.Reject(ChatTurnErrorCode.InvalidArgument, $"Tin nhắn vượt quá {MaxMessageLength} ký tự.");
        }

        if (EmailPattern.IsMatch(message) || PhonePattern.IsMatch(message) ||
            DigitRunPattern.IsMatch(message) || LooksLikeAddress(message))
        {
            return InputGuardResult.Reject(ChatTurnErrorCode.PiiRejected, PiiRejectedMessage);
        }

        var containsValidCustomerId = CustomerIdPattern.IsMatch(message);
        var hasCrmIntentSignal = ContainsCrmIntentKeyword(message) || CapitalizedRunPattern.IsMatch(message);

        if (hasCrmIntentSignal && !containsValidCustomerId)
        {
            return InputGuardResult.Reject(ChatTurnErrorCode.CustomerIdRequired, CustomerIdRequiredMessage);
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
