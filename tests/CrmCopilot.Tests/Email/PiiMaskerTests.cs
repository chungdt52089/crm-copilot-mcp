using CrmCopilot.Contracts.Crm;
using CrmCopilot.McpServer.Email;

namespace CrmCopilot.Tests.Email;

/// <summary>
/// Unit coverage of PiiMasker.Mask against docs/08_RAG_EMAIL_AND_PII_SPEC.md §11's required list
/// ("mask field-based từng loại", "regex fallback cho dữ liệu lẫn trong text") plus the P0-07
/// amendment's objective-masking requirement (✏️1).
/// </summary>
public class PiiMaskerTests
{
    private static readonly CustomerDto Customer = new(
        "CUS-0001", "Nguyễn Minh Anh", "minh.anh@example.test", "0900000001", "000000000001",
        "Priority", "Hà Nội", "vi", "RM-0001", "Active", true, DateTime.UtcNow);

    private static InteractionDto Interaction(string summary, string outcome, string? nextAction = null) =>
        new("INT-0001", Customer.Id, "Call", DateTime.UtcNow, summary, outcome, nextAction, true);

    [Fact]
    public void Mask_CustomerFullNameInInteractionText_ReplacedWithNamePlaceholder()
    {
        var interaction = Interaction($"Đã gọi cho {Customer.FullName} để trao đổi.", "outcome");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("{{CUSTOMER_NAME}}", result.Interactions[0].MaskedSummary);
        Assert.DoesNotContain(Customer.FullName, result.Interactions[0].MaskedSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_CustomerEmailInInteractionText_ReplacedWithEmailPlaceholder()
    {
        var interaction = Interaction($"Email liên hệ: {Customer.Email}", "outcome");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("[redacted-email]", result.Interactions[0].MaskedSummary);
        Assert.DoesNotContain(Customer.Email, result.Interactions[0].MaskedSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_CustomerPhoneInInteractionText_ReplacedWithPhonePlaceholder()
    {
        var interaction = Interaction($"Số điện thoại: {Customer.Phone}", "outcome");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("[redacted-phone]", result.Interactions[0].MaskedSummary);
        Assert.DoesNotContain(Customer.Phone, result.Interactions[0].MaskedSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_CustomerAccountReferenceInInteractionText_ReplacedWithAccountPlaceholder()
    {
        var interaction = Interaction($"Số tài khoản: {Customer.AccountReference}", "outcome");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("[redacted-account]", result.Interactions[0].MaskedSummary);
        Assert.DoesNotContain(Customer.AccountReference, result.Interactions[0].MaskedSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_UnrelatedEmailShapedText_RedactedByRegexFallback()
    {
        var interaction = Interaction("summary", "liên hệ qua other.person@example.com");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("[redacted-email]", result.Interactions[0].MaskedOutcome);
        Assert.DoesNotContain("other.person@example.com", result.Interactions[0].MaskedOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_UnrelatedPhoneShapedText_RedactedByRegexFallback()
    {
        var interaction = Interaction("summary", "outcome", "Gọi lại số 0912345678 sau");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("[redacted-phone]", result.Interactions[0].MaskedNextAction);
        Assert.DoesNotContain("0912345678", result.Interactions[0].MaskedNextAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_UnrelatedDigitRunText_RedactedByRegexFallback()
    {
        var interaction = Interaction("Mã hồ sơ liên quan: 987654321099", "outcome");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("[redacted-account]", result.Interactions[0].MaskedSummary);
        Assert.DoesNotContain("987654321099", result.Interactions[0].MaskedSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_SecretTokenShapedText_RedactedByRegexFallback()
    {
        const string secretLike = "AIzaSyDaGmWKa4JsXZHjGw7ISLn3namBGewQe";
        var interaction = Interaction($"Ghi chú nội bộ có chứa token {secretLike} không nên xuất hiện.", "outcome");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("[REDACTED_SECRET]", result.Interactions[0].MaskedSummary);
        Assert.DoesNotContain(secretLike, result.Interactions[0].MaskedSummary, StringComparison.Ordinal);
        Assert.Contains("secret", result.MaskedFieldTypes);
    }

    [Fact]
    public void Mask_PhoneBeforeDigitRun_PhoneNotDoubleRedactedAsAccount()
    {
        var interaction = Interaction("Liên hệ số 0912345678 khi cần.", "outcome");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains("[redacted-phone]", result.Interactions[0].MaskedSummary);
        Assert.DoesNotContain("[redacted-account]", result.Interactions[0].MaskedSummary);
    }

    [Fact]
    public void Mask_ObjectiveContainingCustomerFullName_ReplacedWithNamePlaceholder()
    {
        var result = PiiMasker.Mask(Customer, [], $"Soạn email cho {Customer.FullName} về sản phẩm tiết kiệm.");

        Assert.Contains("{{CUSTOMER_NAME}}", result.MaskedObjective);
        Assert.DoesNotContain(Customer.FullName, result.MaskedObjective, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_ObjectiveContainingEmailPhoneAccountSecret_AllRedactedByRegexFallback()
    {
        const string secretLike = "AIzaSyDaGmWKa4JsXZHjGw7ISLn3namBGewQe";
        var objective =
            $"Liên hệ qua stray.contact@example.com hoặc 0987654321, số tài khoản 000111222333, token {secretLike}.";

        var result = PiiMasker.Mask(Customer, [], objective);

        Assert.Contains("[redacted-email]", result.MaskedObjective);
        Assert.Contains("[redacted-phone]", result.MaskedObjective);
        Assert.Contains("[redacted-account]", result.MaskedObjective);
        Assert.Contains("[REDACTED_SECRET]", result.MaskedObjective);
        Assert.DoesNotContain("stray.contact@example.com", result.MaskedObjective, StringComparison.Ordinal);
        Assert.DoesNotContain("0987654321", result.MaskedObjective, StringComparison.Ordinal);
        Assert.DoesNotContain(secretLike, result.MaskedObjective, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_NoInteractionsAndCleanObjective_MaskedFieldTypesAlwaysHasFourUnconditionalEntriesNeverSecret()
    {
        var result = PiiMasker.Mask(Customer, [], "Một objective sạch, không có PII nào cả.");

        Assert.Equal(["name", "email", "phone", "accountReference"], result.MaskedFieldTypes);
    }

    [Fact]
    public void Mask_RetrievalQuerySummaries_ContainsOnlyMaskedSummaryPerInteraction()
    {
        var interaction = Interaction($"Tên khách: {Customer.FullName}", "outcome không liên quan");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Single(result.RetrievalQuerySummaries);
        Assert.Contains("{{CUSTOMER_NAME}}", result.RetrievalQuerySummaries[0]);
    }

    [Fact]
    public void Mask_CustomerSegment_NeverMaskedAnywhereInOutput()
    {
        // Segment isn't a masking input at all (doc08 §6 allows it through unmasked) — this test
        // documents that PiiMasker has no code path that even touches it, by construction (the
        // Segment value never appears in any masked output field here).
        var interaction = Interaction($"Phân khúc: {Customer.Segment}", "outcome");

        var result = PiiMasker.Mask(Customer, [interaction], "objective");

        Assert.Contains(Customer.Segment, result.Interactions[0].MaskedSummary, StringComparison.Ordinal);
    }
}
