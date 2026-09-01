using CrmCopilot.Web.Chat;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// P0-06 direct unit tests for the storage-boundary redaction step. Assertions are specific
/// (exact placeholder present, exact original PII value absent) rather than a blanket "no digits"
/// check — CUS-0001 itself contains digits and must survive untouched.
/// </summary>
public class ConversationMessageSanitizerTests
{
    [Fact]
    public void Sanitize_EmailPresent_ReplacedWithPlaceholder()
    {
        var result = ConversationMessageSanitizer.Sanitize("Email của khách là minh.anh@example.test nhé");

        Assert.Contains("[redacted-email]", result);
        Assert.DoesNotContain("minh.anh@example.test", result);
    }

    [Fact]
    public void Sanitize_PhonePresent_ReplacedWithPlaceholder()
    {
        var result = ConversationMessageSanitizer.Sanitize("Số điện thoại: 0900000001");

        Assert.Contains("[redacted-phone]", result);
        Assert.DoesNotContain("0900000001", result);
    }

    [Fact]
    public void Sanitize_LongDigitRunPresent_ReplacedWithPlaceholder()
    {
        var result = ConversationMessageSanitizer.Sanitize("Số tài khoản: 000000000001");

        Assert.Contains("[redacted-account]", result);
        Assert.DoesNotContain("000000000001", result);
    }

    [Fact]
    public void Sanitize_CustomerIdToken_SurvivesAlongsideRedactedPii()
    {
        var result = ConversationMessageSanitizer.Sanitize(
            "Khách hàng CUS-0001, số tài khoản 000000000001, email minh.anh@example.test");

        Assert.Contains("CUS-0001", result);
        Assert.Contains("[redacted-account]", result);
        Assert.Contains("[redacted-email]", result);
        Assert.DoesNotContain("000000000001", result);
        Assert.DoesNotContain("minh.anh@example.test", result);
    }

    [Fact]
    public void Sanitize_NoPii_ReturnsMessageUnchanged()
    {
        const string message = "Khách hàng này có tương tác gì gần đây?";

        var result = ConversationMessageSanitizer.Sanitize(message);

        Assert.Equal(message, result);
    }
}
