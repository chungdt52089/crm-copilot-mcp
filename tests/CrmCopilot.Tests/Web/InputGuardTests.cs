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
}
