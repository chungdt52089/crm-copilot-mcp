using System.Text.RegularExpressions;

namespace CrmCopilot.Web.Speech;

/// <summary>
/// P0-15 (WP4). Rewrites a spoken customer id into the canonical <c>CUS-####</c> shape.
///
/// Why this exists: Spike A observed that the model returns the id spelled out — "C U S 0 0 0 1" —
/// or run together as "CUS0001". Neither contains the <c>CUS-\d{4}</c> token InputGuard looks for, so
/// without this step every dictated lookup would come back CUSTOMER_ID_REQUIRED and the RM would
/// retype the id by hand each time.
///
/// It runs on the SERVER, inside the transcribe endpoint, so it is a pure function under unit test
/// rather than untested browser code.
///
/// DELIBERATELY NARROW (Product Owner decision, this checkpoint):
///  - digit tokens are only read immediately after a CUS token, never anywhere else in the sentence.
///    "không" is the digit 0 AND the ordinary Vietnamese word for "no", so a broader rule would
///    corrupt sentences like "khách hàng này không có tương tác";
///  - the run must be EXACTLY four tokens. Three or five leaves the whole span untouched rather than
///    guessing at a padded or truncated id.
///
/// It never loosens validation: whatever it produces still faces CustomerIdFormat/InputGuard on the
/// chat path, and text it declines to rewrite is passed through verbatim for the RM to fix.
/// </summary>
internal static class TranscriptNormalizer
{
    /// <summary>Whitespace, dots and hyphens are all separators a dictated id can arrive with
    /// ("C U S", "C.U.S", "CUS-0001"); zero-or-more so "CUS0001" matches too.</summary>
    private const string Separator = @"[\s.\-]*";

    /// <summary>One ASCII digit, or one Vietnamese number word. The trailing letter-lookahead stops
    /// a short word matching inside a longer one ("ba" inside "bay").</summary>
    private const string DigitToken =
        @"(?:\d|(?:không|khong|một|mốt|mot|hai|ba|bốn|bon|tư|tu|năm|nam|lăm|lam|sáu|sau|bảy|bay|tám|tam|chín|chin)(?![\p{L}]))";

    /// <summary>
    /// CUS (possibly spelled out) followed by exactly four digit tokens.
    ///
    /// The leading lookbehind stops "focus" matching; the trailing lookaheads are what enforce
    /// "exactly four" — one rejects a fifth token, the other rejects a trailing letter/digit, so
    /// "cus 00012" and "CUSTOMER" both fall through untouched.
    /// </summary>
    private static readonly Regex CustomerIdShape = new(
        $@"(?<![\p{{L}}])C{Separator}U{Separator}S{Separator}" +
        $@"({DigitToken}){Separator}({DigitToken}){Separator}({DigitToken}){Separator}({DigitToken})" +
        $@"(?!{Separator}{DigitToken})(?![\p{{L}}\p{{N}}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, char> WordDigits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["không"] = '0', ["khong"] = '0',
        ["một"] = '1', ["mốt"] = '1', ["mot"] = '1',
        ["hai"] = '2',
        ["ba"] = '3',
        ["bốn"] = '4', ["bon"] = '4', ["tư"] = '4', ["tu"] = '4',
        ["năm"] = '5', ["nam"] = '5', ["lăm"] = '5', ["lam"] = '5',
        ["sáu"] = '6', ["sau"] = '6',
        ["bảy"] = '7', ["bay"] = '7',
        ["tám"] = '8', ["tam"] = '8',
        ["chín"] = '9', ["chin"] = '9',
    };

    /// <summary>
    /// Returns the transcript with every recognized spoken customer id rewritten to CUS-####.
    /// Everything else — including a run that is not exactly four tokens — is returned verbatim.
    /// Idempotent: an already-canonical "CUS-0001" re-normalizes to itself.
    /// </summary>
    public static string Normalize(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return transcript ?? string.Empty;
        }

        return CustomerIdShape.Replace(transcript, ToCanonicalId);
    }

    private static string ToCanonicalId(Match match)
    {
        var digits = new char[4];

        for (var i = 0; i < digits.Length; i++)
        {
            var token = match.Groups[i + 1].Value;

            if (token.Length == 1 && char.IsAsciiDigit(token[0]))
            {
                digits[i] = token[0];
            }
            else if (WordDigits.TryGetValue(token, out var digit))
            {
                digits[i] = digit;
            }
            else
            {
                // Unreachable while DigitToken and WordDigits agree, but the two are separate
                // declarations: if they ever drift, pass the span through untouched rather than
                // emitting a half-built id.
                return match.Value;
            }
        }

        return $"CUS-{new string(digits)}";
    }
}
