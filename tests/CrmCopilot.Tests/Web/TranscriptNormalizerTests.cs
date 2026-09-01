using CrmCopilot.Web.Speech;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// P0-15 (WP4). The normalizer is a pure function precisely so it can be pinned here rather than
/// inferred from browser behaviour. The negative rows matter as much as the positive ones: the rule
/// is deliberately narrow, and these prove it does not fire where it must not.
/// </summary>
public class TranscriptNormalizerTests
{
    [Theory]
    // Spike A's actual output shape — the priority case.
    [InlineData("Tìm hồ sơ khách hàng C U S 0 0 0 1", "Tìm hồ sơ khách hàng CUS-0001")]
    // Spike A's other observed shape, with the trailing sentence punctuation preserved.
    [InlineData("Tìm hồ sơ khách hàng CUS0001.", "Tìm hồ sơ khách hàng CUS-0001.")]
    [InlineData("cus 0001", "CUS-0001")]
    // Secondary path: Vietnamese number words, only because they follow a CUS token.
    [InlineData("cus không không không một", "CUS-0001")]
    // Already canonical — must survive untouched.
    [InlineData("CUS-0001", "CUS-0001")]
    // "không" here means "no", not zero. Nothing follows a CUS, so nothing is rewritten.
    [InlineData("khách hàng này không có tương tác", "khách hàng này không có tương tác")]
    // Three digits: not exactly four, so the span is left alone rather than padded.
    [InlineData("cus 001", "cus 001")]
    // Five digits: not exactly four, so the span is left alone rather than truncated.
    [InlineData("cus 00012", "cus 00012")]
    public void Normalize_RewritesOnlyAnExactFourTokenIdAfterCus(string input, string expected) =>
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));

    /// <summary>
    /// Normalizing an already-normalized transcript must be a no-op — the endpoint runs this on every
    /// request, and a second pass over "CUS-0001" must not consume the hyphen or re-read the digits.
    /// </summary>
    [Fact]
    public void Normalize_IsIdempotent()
    {
        const string input = "Tìm hồ sơ khách hàng C U S 0 0 0 1";

        var once = TranscriptNormalizer.Normalize(input);
        var twice = TranscriptNormalizer.Normalize(once);

        Assert.Equal("Tìm hồ sơ khách hàng CUS-0001", once);
        Assert.Equal(once, twice);
    }
}
