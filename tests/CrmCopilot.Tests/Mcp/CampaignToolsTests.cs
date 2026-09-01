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
/// Branch coverage for the P0-10 get_campaigns tool. The scoping guarantee (plan D10) is asserted
/// against DatasetCrmGateway and the real checked-in campaign data, because "returns only this
/// customer's campaigns" is only meaningful if there is a campaign in the dataset that the customer
/// is NOT eligible for.
/// </summary>
public class CampaignToolsTests
{
    private static CampaignTools CreateTools(ICrmGateway gateway) =>
        new(gateway, new HttpContextAccessor(), NullLogger<CampaignTools>.Instance);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task GetCampaigns_BlankCustomerId_ReturnsInvalidArgument()
    {
        var tools = CreateTools(new FakeCrmGateway());

        var result = await tools.GetCampaigns("  ", 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task GetCampaigns_LimitOutOfRange_ReturnsInvalidArgument(int limit)
    {
        var tools = CreateTools(new FakeCrmGateway());

        var result = await tools.GetCampaigns("CUS-0001", limit, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCampaigns_CustomerNotFound_ReturnsNotFound()
    {
        var tools = CreateTools(new DatasetCrmGateway());

        var result = await tools.GetCampaigns(ScenarioDatasetSeed.MissingCustomerId, 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.NotFound, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task GetCampaigns_UpstreamFailure_ReturnsUpstreamUnavailableWithoutLeakingTheMessage()
    {
        var gateway = new FakeCrmGateway
        {
            ThrowOnGetCampaigns = new CrmUpstreamException("boom", retryable: true, traceId: null),
        };
        var tools = CreateTools(gateway);

        var result = await tools.GetCampaigns("CUS-0001", 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.UpstreamUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.True(root.GetProperty("error").GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain("boom", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Plan D10, the reason this tool has no list-everything mode: the canonical customer must get
    /// back strictly fewer campaigns than the dataset holds, and every one must actually list them.
    /// </summary>
    [Fact]
    public async Task GetCampaigns_ReturnsOnlyCampaignsThisCustomerIsEligibleFor()
    {
        var dataset = ScenarioDatasetSeed.Dataset;
        var tools = CreateTools(new DatasetCrmGateway());

        var result = await tools.GetCampaigns(ScenarioDatasetSeed.CanonicalCustomerId, 20, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());

        var campaigns = root.GetProperty("data").GetProperty("campaigns");
        Assert.True(campaigns.GetArrayLength() > 0, "The canonical customer must belong to at least one campaign.");
        Assert.True(
            campaigns.GetArrayLength() < dataset.Campaigns.Count,
            "At least one campaign must exclude the canonical customer, or this assertion proves nothing.");

        foreach (var campaign in campaigns.EnumerateArray())
        {
            var id = campaign.GetProperty("id").GetString();
            var source = dataset.Campaigns.Single(c => c.Id == id);
            Assert.Contains(ScenarioDatasetSeed.CanonicalCustomerId, source.EligibleCustomerIds);
        }
    }

    [Fact]
    public async Task GetCampaigns_Success_EmitsOneSourceIdPerCampaign()
    {
        var tools = CreateTools(new DatasetCrmGateway());

        var result = await tools.GetCampaigns(ScenarioDatasetSeed.CanonicalCustomerId, 20, TestContext.Current.CancellationToken);

        var root = Parse(result);
        var campaigns = root.GetProperty("data").GetProperty("campaigns");
        var sourceIds = root.GetProperty("sourceIds");

        Assert.Equal(campaigns.GetArrayLength(), sourceIds.GetArrayLength());
        for (var i = 0; i < campaigns.GetArrayLength(); i++)
        {
            Assert.Equal($"crm:campaign:{campaigns[i].GetProperty("id").GetString()}", sourceIds[i].GetString());
        }
    }

    [Fact]
    public async Task GetCampaigns_LimitIsApplied()
    {
        var tools = CreateTools(new DatasetCrmGateway());

        var result = await tools.GetCampaigns(ScenarioDatasetSeed.CanonicalCustomerId, 1, TestContext.Current.CancellationToken);

        Assert.Equal(1, Parse(result).GetProperty("data").GetProperty("campaigns").GetArrayLength());
    }

    [Fact]
    public async Task GetCampaigns_CustomerInNoCampaign_ReturnsEmptySuccessNotNotFound()
    {
        var dataset = ScenarioDatasetSeed.Dataset;
        var orphan = dataset.Customers.FirstOrDefault(customer =>
            !dataset.Campaigns.Any(campaign => campaign.EligibleCustomerIds.Contains(customer.Id)));

        Assert.SkipWhen(orphan is null, "Dataset has no customer outside every campaign; nothing to assert here.");

        var tools = CreateTools(new DatasetCrmGateway());

        var result = await tools.GetCampaigns(orphan!.Id, 5, TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("data").GetProperty("campaigns").GetArrayLength());
    }
}
