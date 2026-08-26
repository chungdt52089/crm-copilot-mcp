using CrmCopilot.Contracts.Chat;
using CrmCopilot.Web.Chat;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// Direct unit tests for InputGuard (plan D7). Mechanism-1 values are the real CUS-0001 fixture
/// values from data/crm/customers.json — not the "ACC-0001"-shaped test-double constant
/// McpToolProtocolTests.cs uses for its own, unrelated purpose. The address category has no real
/// fixture in this dataset (CustomerDto has no Address field) and uses a constructed example.
/// </summary>
public class InputGuardTests
{
    // Values below are copied literally from CUS-0001's real fixture (data/crm/customers.json:
    // email="minh.anh@example.test", phone="0900000001", accountReference="000000000001" — a
    // 12-digit string, not the "ACC-0001"-shaped test-double constant McpToolProtocolTests.cs uses
    // for its own, unrelated purpose). The address example is constructed — no Address field
    // exists anywhere in this dataset's data model.
    [Theory]
    [InlineData("Email của tôi là minh.anh@example.test")]
    [InlineData("Số điện thoại: 0900000001")]
    [InlineData("Số tài khoản: 000000000001")]
    [InlineData("Địa chỉ nhà tôi: 123 Đường Láng, Phường Láng Hạ, Quận Đống Đa, Hà Nội")]
    public void Validate_MechanicalPiiCategory_Rejected(string message)
    {
        var result = InputGuard.Validate(message);

        Assert.False(result.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.PiiRejected, result.ErrorCode);
    }

    [Fact]
    public void Validate_CrmKeywordWithoutCustomerId_RejectedAsCustomerIdRequired()
    {
        var result = InputGuard.Validate("Tìm khách hàng giúp tôi");

        Assert.False(result.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, result.ErrorCode);
    }

    [Fact]
    public void Validate_CapitalizedNameRunWithoutCustomerId_RejectedAsCustomerIdRequired()
    {
        var result = InputGuard.Validate("Nguyễn Minh Anh muốn biết thêm về sản phẩm tiết kiệm");

        Assert.False(result.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, result.ErrorCode);
    }

    [Fact]
    public void Validate_CrmKeywordWithValidCustomerId_Allowed()
    {
        var result = InputGuard.Validate("Tìm khách hàng CUS-0001");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Validate_CapitalizedNameRunWithValidCustomerId_Allowed()
    {
        var result = InputGuard.Validate("Khách hàng CUS-0001 (Nguyễn Minh Anh) quan tâm gửi tiết kiệm");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Validate_GenericMessageWithNoCrmIntentSignal_Allowed()
    {
        var result = InputGuard.Validate("Sản phẩm tiết kiệm 6 tháng có đặc điểm gì?");

        Assert.True(result.IsAllowed);
    }

    // --- P0-06: keyword-only follow-up resolution against conversation state ---

    [Fact]
    public void Validate_CrmKeywordFollowUp_WithActiveCustomer_Allowed()
    {
        var result = InputGuard.Validate("Khách hàng này có tương tác gì gần đây?", currentCustomerId: "CUS-0001");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Validate_CrmKeywordFollowUp_WithoutActiveCustomer_RejectedAsCustomerIdRequired()
    {
        var result = InputGuard.Validate("Khách hàng này có tương tác gì gần đây?", currentCustomerId: null);

        Assert.False(result.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, result.ErrorCode);
    }

    [Fact]
    public void Validate_CapitalizedNameRun_WithActiveCustomer_StillRejected()
    {
        // A literal name mention is a raw-name leak risk, not a pronoun follow-up — always
        // rejected regardless of session state, even when a customer is already active.
        var result = InputGuard.Validate("Nguyễn Văn A cần được gọi lại", currentCustomerId: "CUS-0001");

        Assert.False(result.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.CustomerIdRequired, result.ErrorCode);
    }

    [Fact]
    public void Validate_BlankMessage_RejectedAsInvalidArgument()
    {
        var result = InputGuard.Validate("   ");

        Assert.False(result.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public void Validate_MessageOverMaxLength_RejectedAsInvalidArgument()
    {
        var result = InputGuard.Validate(new string('a', 2001));

        Assert.False(result.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.InvalidArgument, result.ErrorCode);
    }

    // ---- P0-10: malformed customer identifier (browser-verified) --------------------------------

    /// <summary>
    /// The guard must fire whether or not a session customer exists. With one, the old code treated
    /// "khách hàng ..." as a resolvable follow-up and waved the whole message — typo included —
    /// through to Gemini, which then substituted the session customer and reported success.
    /// </summary>
    [Theory]
    [InlineData("Tra cứu khách hàng CS-0002")]
    [InlineData("Tra cứu khách hàng CS-0003")]
    [InlineData("Tra cứu khách hàng CS-0004")]
    public void Validate_MalformedCustomerId_RejectedRegardlessOfSessionState(string message)
    {
        var fresh = InputGuard.Validate(message);
        var withSession = InputGuard.Validate(message, currentCustomerId: "CUS-0002");

        Assert.False(fresh.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.CustomerIdInvalid, fresh.ErrorCode);
        Assert.False(withSession.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.CustomerIdInvalid, withSession.ErrorCode);
    }

    /// <summary>A malformed id is a different failure from "no id supplied" — and must not be
    /// reported as the latter, which would tell the RM to do what they just did.</summary>
    [Fact]
    public void Validate_MalformedCustomerId_IsNotReportedAsCustomerIdRequired()
    {
        var result = InputGuard.Validate("Tra cứu khách hàng CS-0003");

        Assert.NotEqual(ChatTurnErrorCode.CustomerIdRequired, result.ErrorCode);
        Assert.Equal("Mã khách hàng không hợp lệ. Vui lòng kiểm tra đúng định dạng và thử lại.", result.ErrorMessage);
    }

    /// <summary>
    /// The public message must not teach the id convention. Spelling the pattern out in an error
    /// hands a caller the shape of every valid customer key; the rule belongs in the validator, the
    /// tests and docs/07 — none of which an end user sees.
    /// </summary>
    [Fact]
    public void Validate_MalformedCustomerId_PublicMessageLeaksNoFormatConvention()
    {
        var message = InputGuard.Validate("Tra cứu khách hàng CS-0003").ErrorMessage!;

        Assert.DoesNotContain("CUS-", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("####", message, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\d", message, StringComparison.Ordinal);
        Assert.DoesNotContain("regex", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CustomerIdFormat", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_MessageMixingAValidAndAMalformedId_IsRejected()
    {
        var result = InputGuard.Validate("So sánh khách hàng CUS-0001 với CS-0003");

        Assert.False(result.IsAllowed);
        Assert.Equal(ChatTurnErrorCode.CustomerIdInvalid, result.ErrorCode);
    }

    /// <summary>
    /// The guard is narrow by design: the other identifier families in this system must pass. Product
    /// codes and template/call-script ids do not even match the customer-id shape (they end in three
    /// digits plus a letter, or two digits); OPP/INT/CMP/ACC/RM do match it and are allowlisted.
    /// </summary>
    [Theory]
    [InlineData("Khách hàng CUS-0002 có cơ hội OPP-0002 nào không?")]
    [InlineData("Xem tương tác INT-0001 của khách hàng CUS-0002")]
    [InlineData("Chiến dịch CMP-0001 của khách hàng CUS-0002")]
    [InlineData("Soạn email cho khách hàng CUS-0002 về sản phẩm PRD-SAV-006M")]
    [InlineData("Dùng mẫu TPL-EMAIL-MATURITY-01 cho khách hàng CUS-0002")]
    [InlineData("Kịch bản CS-CALL-SAVINGS-FOLLOWUP-01 cho khách hàng CUS-0002")]
    public void Validate_OtherIdentifierFamilies_AreAllowed(string message)
    {
        var result = InputGuard.Validate(message, currentCustomerId: "CUS-0002");

        Assert.True(result.IsAllowed, $"Message was wrongly rejected as {result.ErrorCode}: {message}");
    }

    [Fact]
    public void Validate_WellFormedCustomerId_StillAllowed()
    {
        Assert.True(InputGuard.Validate("Tìm khách hàng CUS-0002").IsAllowed);
        // Well-formed but nonexistent is a lookup outcome, not an input-shape problem.
        Assert.True(InputGuard.Validate("Tìm khách hàng CUS-9999").IsAllowed);
    }
}
