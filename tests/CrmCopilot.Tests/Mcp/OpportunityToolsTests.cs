using System.Text.Json;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Crm;
using CrmCopilot.Tests.Acceptance.TestSupport;
using CrmCopilot.Tests.Crm.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.Mcp;

/// <summary>
/// Branch coverage for the P0-10 get_opportunities tool (plan Amendment A1). Validation branches
/// run against FakeCrmGateway; the ordering/filter-before-limit contract is asserted against
/// DatasetCrmGateway so it exercises the real checked-in dataset and the real CrmDataset query
/// rather than a stub that could be made to agree with the test.
/// </summary>
public class OpportunityToolsTests
{
    private static OpportunityTools CreateTools(ICrmGateway gateway) =>
        new(gateway, new HttpContextAccessor(), NullLogger<OpportunityTools>.Instance);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task GetOpportunities_BlankCustomerId_ReturnsInvalidArgument()
    {
        var tools = CreateTools(new FakeCrmGateway());

        var result = await tools.GetOpportunities("  ", null, 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task GetOpportunities_LimitOutOfRange_ReturnsInvalidArgument(int limit)
    {
        var tools = CreateTools(new FakeCrmGateway());

        var result = await tools.GetOpportunities("CUS-0001", null, limit, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>Amendment A7-2.</summary>
    [Fact]
    public async Task GetOpportunities_InvalidStatus_ReturnsInvalidArgumentAndNeverCallsGateway()
    {
        var gateway = new FakeCrmGateway();
        var tools = CreateTools(gateway);

        var result = await tools.GetOpportunities("CUS-0001", "KhongHopLe", 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Null(gateway.LastOpportunitiesCustomerId);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("OPEN")]
    [InlineData("Open")]
    public async Task GetOpportunities_StatusIsCaseInsensitive_AndReachesGatewayCanonicalized(string status)
    {
        var gateway = new FakeCrmGateway();
        var tools = CreateTools(gateway);

        await tools.GetOpportunities("CUS-0001", status, 5, TestContext.Current.CancellationToken);

        Assert.Equal(OpportunityStatuses.Open, gateway.LastOpportunitiesStatus);
    }

    [Fact]
    public async Task GetOpportunities_NoStatus_PassesNullFilterThrough()
    {
        var gateway = new FakeCrmGateway();
        var tools = CreateTools(gateway);

        await tools.GetOpportunities("CUS-0001", null, 5, TestContext.Current.CancellationToken);

        Assert.Null(gateway.LastOpportunitiesStatus);
    }

    [Fact]
    public async Task GetOpportunities_CustomerNotFound_ReturnsNotFound()
    {
        var gateway = new FakeCrmGateway { ThrowOnGetOpportunities = new CrmNotFoundException("CUS-9999", null) };
        var tools = CreateTools(gateway);

        var result = await tools.GetOpportunities("CUS-9999", null, 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task GetOpportunities_UpstreamFailure_ReturnsUpstreamUnavailableAndPreservesRetryable()
    {
        var gateway = new FakeCrmGateway
        {
            ThrowOnGetOpportunities = new CrmUpstreamException("boom", retryable: true, traceId: null),
        };
        var tools = CreateTools(gateway);

        var result = await tools.GetOpportunities("CUS-0001", null, 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.UpstreamUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());

        // The upstream exception's own message must never surface in the tool response.
        Assert.DoesNotContain("boom", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOpportunities_Success_EmitsOneSourceIdPerOpportunity()
    {
        var tools = CreateTools(new DatasetCrmGateway());

        var result = await tools.GetOpportunities(
            ScenarioDatasetSeed.CanonicalCustomerId, null, 20, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());

        var opportunities = root.GetProperty("data").GetProperty("opportunities");
        var sourceIds = root.GetProperty("sourceIds");
        Assert.Equal(opportunities.GetArrayLength(), sourceIds.GetArrayLength());

        for (var i = 0; i < opportunities.GetArrayLength(); i++)
        {
            Assert.Equal($"crm:opportunity:{opportunities[i].GetProperty("id").GetString()}", sourceIds[i].GetString());
        }
    }

    /// <summary>Amendment A7-1 — a status filter must exclude every other status.</summary>
    [Fact]
    public async Task GetOpportunities_StatusOpen_NeverReturnsWonLostOrClosed()
    {
        var tools = CreateTools(new DatasetCrmGateway());

        var result = await tools.GetOpportunities(
            ScenarioDatasetSeed.CanonicalCustomerId, OpportunityStatuses.Open, 20, TestContext.Current.CancellationToken);

        var opportunities = Parse(result).GetProperty("data").GetProperty("opportunities");
        Assert.True(opportunities.GetArrayLength() > 0, "The canonical customer must have at least one Open opportunity.");

        foreach (var opportunity in opportunities.EnumerateArray())
        {
            Assert.Equal(OpportunityStatuses.Open, opportunity.GetProperty("status").GetString());
        }
    }

    /// <summary>
    /// Amendment A7-3, the regression this ordering rule exists for. The canonical customer's Won
    /// opportunity carries the EARLIER ExpectedCloseDateUtc, so it sorts first; an implementation
    /// that applied Take(limit) before the status filter would take that record, filter it away,
    /// and answer "no open opportunities" for a customer that plainly has one.
    /// </summary>
    [Fact]
    public async Task GetOpportunities_StatusOpenWithLimitOne_FiltersBeforeLimiting()
    {
        var tools = CreateTools(new DatasetCrmGateway());

        var unfiltered = Parse(await tools.GetOpportunities(
            ScenarioDatasetSeed.CanonicalCustomerId, null, 1, TestContext.Current.CancellationToken));
        var firstUnfiltered = unfiltered.GetProperty("data").GetProperty("opportunities")[0];

        // Guards the premise: without a filter the first record really is a non-Open one.
        Assert.NotEqual(OpportunityStatuses.Open, firstUnfiltered.GetProperty("status").GetString());

        var filtered = Parse(await tools.GetOpportunities(
            ScenarioDatasetSeed.CanonicalCustomerId, OpportunityStatuses.Open, 1, TestContext.Current.CancellationToken));
        var opportunities = filtered.GetProperty("data").GetProperty("opportunities");

        Assert.Equal(1, opportunities.GetArrayLength());
        Assert.Equal(OpportunityStatuses.Open, opportunities[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetOpportunities_CustomerWithNoOpportunities_ReturnsEmptySuccessNotNotFound()
    {
        var tools = CreateTools(new DatasetCrmGateway());

        var result = await tools.GetOpportunities("CUS-0004", null, 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("data").GetProperty("opportunities").GetArrayLength());
        Assert.Equal(0, root.GetProperty("sourceIds").GetArrayLength());
    }
}
