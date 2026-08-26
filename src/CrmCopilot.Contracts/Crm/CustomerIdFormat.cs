using System.Text.RegularExpressions;

namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// The canonical customer identifier shape, <c>CUS-####</c> (docs/06_DATA_AND_MOCK_API_SPEC.md §3).
///
/// Lives in Contracts because two processes that do not reference each other both need it:
/// CrmCopilot.Web's InputGuard rejects a malformed identifier before Gemini ever sees the message,
/// and CrmCopilot.McpServer's get_customer re-validates at the tool boundary as defense in depth.
/// Same reasoning as <see cref="ProductCodeFormat"/>.
/// </summary>
public static class CustomerIdFormat
{
    /// <summary>
    /// The public, RM-facing message for a refused identifier — the single text used by both the
    /// Host (CUSTOMER_ID_INVALID) and the get_customer tool result.
    ///
    /// Deliberately says nothing about the id convention. Spelling the pattern out in a public error
    /// hands an unauthenticated caller the shape of every valid customer key, which is exactly the
    /// probing aid an error message should not be. The rule itself stays where it belongs — in
    /// <see cref="CanonicalPattern"/>, in the tests, and in docs/07 — none of which reach an end
    /// user. A valid id such as CUS-0002 may still appear in customer data, chat history and the
    /// stale-data notice; this restriction is about FORMAT GUIDANCE in errors, not about ids.
    /// </summary>
    public const string InvalidMessage = "Mã khách hàng không hợp lệ. Vui lòng kiểm tra đúng định dạng và thử lại.";

    /// <summary>
    /// Case-insensitive on purpose: InputGuard has always treated <c>cus-0001</c> as a customer-id
    /// mention, and tightening that here would be an unrelated behaviour change. A wrong-cased id
    /// still reaches the gateway and answers NOT_FOUND, which is the honest outcome — the shape is
    /// right, the record is not there.
    /// </summary>
    private static readonly Regex CanonicalPattern = new(@"^CUS-\d{4}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Tokens shaped like a customer id: 2-4 letters, a hyphen, exactly four digits. Deliberately
    /// narrow so it cannot swallow the other identifier families that legitimately appear in a chat
    /// message — product codes end in three digits plus a letter (<c>PRD-SAV-006M</c>), template and
    /// call-script ids end in two (<c>TPL-EMAIL-MATURITY-01</c>, <c>CS-CALL-SAVINGS-FOLLOWUP-01</c>),
    /// so none of them match this at all.
    /// </summary>
    private static readonly Regex CustomerIdLikePattern = new(@"\b([A-Za-z]{2,4})-(\d{4})\b", RegexOptions.Compiled);

    /// <summary>
    /// Prefixes that are real identifiers in this system but are not customer ids. A message
    /// mentioning one of these is not a malformed customer id and must pass through untouched.
    /// </summary>
    private static readonly HashSet<string> NonCustomerPrefixes =
        new(["OPP", "INT", "CMP", "ACC", "RM"], StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string? customerId) =>
        !string.IsNullOrWhiteSpace(customerId) && CanonicalPattern.IsMatch(customerId);

    /// <summary>
    /// Finds the first customer-id-like token in free text that is NOT a valid customer id and NOT
    /// another known identifier family — i.e. a clear typo such as <c>CS-0002</c>.
    ///
    /// This exists because the damage such a token causes is invisible without it: passed on to the
    /// model it gets silently "corrected" to the session's customer, or forwarded as a lookup that
    /// answers NOT_FOUND (implying the id was well-formed and simply absent), or turned into a
    /// name query that then collides with the session id the Host injects. All three mislead the RM.
    /// </summary>
    public static bool TryFindMalformedToken(string? text, out string malformedToken)
    {
        malformedToken = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (Match match in CustomerIdLikePattern.Matches(text))
        {
            var prefix = match.Groups[1].Value;

            if (string.Equals(prefix, "CUS", StringComparison.OrdinalIgnoreCase) ||
                NonCustomerPrefixes.Contains(prefix))
            {
                continue;
            }

            malformedToken = match.Value;
            return true;
        }

        return false;
    }
}
