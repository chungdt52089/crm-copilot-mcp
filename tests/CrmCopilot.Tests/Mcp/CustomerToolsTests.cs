using System.Text.Json;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Crm;
using CrmCopilot.Tests.Crm.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.Mcp;

/// <summary>
/// Method-level coverage of every branch in the P0-04 plan's CustomerTools mapping tables — fast,
/// offline, against FakeCrmGateway. Protocol-level (real MCP tools/call) coverage of a subset of
/// these lives in McpToolProtocolTests.cs.
/// </summary>
public class CustomerToolsTests
{
    private static readonly CustomerDto Cus0001 = new(
        "CUS-0001", "Nguyễn Minh Anh", "test@example.com", "0900000000", "ACC-0001",
        "Priority", "Hà Nội", "vi", "RM-0001", "Active", true, DateTime.UtcNow);

    private static CustomerTools CreateTools(FakeCrmGateway gateway) =>
        new(gateway, new HttpContextAccessor(), NullLogger<CustomerTools>.Instance);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task GetCustomer_BothArgumentsBlank_ReturnsInvalidArgument()
    {
        var tools = CreateTools(new FakeCrmGateway());

        var result = await tools.GetCustomer(null, null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCustomer_BothArgumentsProvided_ReturnsInvalidArgument()
    {
        var tools = CreateTools(new FakeCrmGateway());

        var result = await tools.GetCustomer("CUS-0001", "Nguyễn Minh Anh", TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCustomer_Found_ReturnsSuccessWithCustomerAndSourceId()
    {
        var gateway = new FakeCrmGateway { FindCustomerResult = CustomerLookupResult.Found(Cus0001) };
        var tools = CreateTools(gateway);

        var result = await tools.GetCustomer("CUS-0001", null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal("CUS-0001", root.GetProperty("data").GetProperty("customer").GetProperty("id").GetString());
        Assert.Equal("crm:customer:CUS-0001", root.GetProperty("sourceIds")[0].GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task GetCustomer_NotFound_ReturnsNotFoundWithErrorCode()
    {
        var gateway = new FakeCrmGateway { FindCustomerResult = CustomerLookupResult.NotFound };
        var tools = CreateTools(gateway);

        var result = await tools.GetCustomer("CUS-9999", null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCustomer_Ambiguous_ReturnsAmbiguousWithCandidatesAndNullError()
    {
        IReadOnlyList<CustomerCandidateDto> candidates =
        [
            new("CUS-0002", "Trần Thị Hương", "Priority", "Hà Nội"),
            new("CUS-0003", "Trần Thị Hương", "Standard", "Đà Nẵng"),
        ];
        var gateway = new FakeCrmGateway { FindCustomerResult = CustomerLookupResult.Ambiguous(candidates) };
        var tools = CreateTools(gateway);

        var result = await tools.GetCustomer(null, "Trần Thị Hương", TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Ambiguous, root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("data").GetProperty("candidates").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetCustomer_UpstreamException_ReturnsUpstreamUnavailableWithMatchingRetryable(bool retryable)
    {
        var gateway = new FakeCrmGateway { ThrowOnFindCustomer = new CrmUpstreamException("boom", retryable, traceId: null) };
        var tools = CreateTools(gateway);

        var result = await tools.GetCustomer("CUS-0001", null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.UpstreamUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(retryable, root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain("boom", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCustomer_UnexpectedException_ReturnsInternalErrorWithoutLeakingExceptionText()
    {
        var gateway = new FakeCrmGateway { ThrowOnFindCustomer = new InvalidOperationException("sensitive-detail") };
        var tools = CreateTools(gateway);

        var result = await tools.GetCustomer("CUS-0001", null, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InternalError, root.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("sensitive-detail", result, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetInteractions_BlankCustomerId_ReturnsInvalidArgument()
    {
        var tools = CreateTools(new FakeCrmGateway());

        var result = await tools.GetInteractions("   ", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task GetInteractions_LimitOutOfRange_ReturnsInvalidArgument(int limit)
    {
        var tools = CreateTools(new FakeCrmGateway());

        var result = await tools.GetInteractions("CUS-0001", limit, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetInteractions_CustomerNotFound_ReturnsNotFound()
    {
        var gateway = new FakeCrmGateway { ThrowOnGetInteractions = new CrmNotFoundException("CUS-9999", traceId: null) };
        var tools = CreateTools(gateway);

        var result = await tools.GetInteractions("CUS-9999", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetInteractions_UpstreamException_ReturnsUpstreamUnavailable()
    {
        var gateway = new FakeCrmGateway { ThrowOnGetInteractions = new CrmUpstreamException("boom", retryable: true, traceId: null) };
        var tools = CreateTools(gateway);

        var result = await tools.GetInteractions("CUS-0001", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.UpstreamUnavailable, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetInteractions_ZeroInteractions_ReturnsSuccessWithEmptyArrayNeverNotFound()
    {
        var gateway = new FakeCrmGateway { InteractionsResult = [] };
        var tools = CreateTools(gateway);

        var result = await tools.GetInteractions("CUS-0001", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("data").GetProperty("interactions").GetArrayLength());
        Assert.Equal(0, root.GetProperty("sourceIds").GetArrayLength());
    }

    [Fact]
    public async Task GetInteractions_Found_ReturnsSuccessWithSourceIdsPerInteraction()
    {
        IReadOnlyList<InteractionDto> interactions =
        [
            new("INT-0001", "CUS-0001", "Call", DateTime.UtcNow, "summary", "outcome", null, true),
        ];
        var gateway = new FakeCrmGateway { InteractionsResult = interactions };
        var tools = CreateTools(gateway);

        var result = await tools.GetInteractions("CUS-0001", 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal("crm:interaction:INT-0001", root.GetProperty("sourceIds")[0].GetString());
        Assert.Equal(5, gateway.LastInteractionsLimit);
    }
}
