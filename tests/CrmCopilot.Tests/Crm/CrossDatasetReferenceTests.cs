using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.MockCrmApi.Data;
using CrmCopilot.McpServer.Knowledge.Ingestion;

namespace CrmCopilot.Tests.Crm;

/// <summary>
/// Referential integrity that spans the CRM dataset and the knowledge dataset (plan D17).
///
/// This lives in the test project on purpose. CrmCopilot.MockCrmApi copies only data/crm/*.json
/// into its own output, so its runtime loader cannot see data/knowledge/products.json and validates
/// productCode shape only. The test project references both source projects, so both data
/// directories are present in ITS output — and both are read through the production loaders using
/// AppContext.BaseDirectory, never the current working directory.
/// </summary>
public class CrossDatasetReferenceTests
{
    private static CrmDataset LoadCrmDataset() => CrmDatasetLoader.LoadFromAppBaseDirectory();

    private static HashSet<string> LoadKnownProductCodes() =>
        KnowledgeSourceLoader.LoadFromAppBaseDirectory()
            .Where(document => document.DocumentType == KnowledgeDocumentType.Product)
            .Select(document => document.ProductCode!)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryOpportunityProductCode_ExistsInTheKnowledgeDataset()
    {
        var knownProductCodes = LoadKnownProductCodes();
        Assert.NotEmpty(knownProductCodes);

        foreach (var opportunity in LoadCrmDataset().Opportunities)
        {
            Assert.Contains(opportunity.ProductCode, knownProductCodes);
        }
    }

    [Fact]
    public void EveryCampaignProductCode_ExistsInTheKnowledgeDataset()
    {
        var knownProductCodes = LoadKnownProductCodes();

        foreach (var campaign in LoadCrmDataset().Campaigns)
        {
            Assert.NotEmpty(campaign.ProductCodes);
            foreach (var productCode in campaign.ProductCodes)
            {
                Assert.Contains(productCode, knownProductCodes);
            }
        }
    }

    [Fact]
    public void EveryCampaignEligibleCustomer_ExistsInTheCrmDataset()
    {
        var dataset = LoadCrmDataset();
        var customerIds = dataset.Customers.Select(customer => customer.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var campaign in dataset.Campaigns)
        {
            foreach (var customerId in campaign.EligibleCustomerIds)
            {
                Assert.Contains(customerId, customerIds);
            }
        }
    }

    /// <summary>
    /// The demo fixtures the P0-10 acceptance gates depend on. Asserted here so dataset drift fails
    /// with a named reason rather than as a confusing scenario mismatch later.
    /// </summary>
    [Fact]
    public void CanonicalCustomer_HasTheExpectedDemoShape()
    {
        var dataset = LoadCrmDataset();

        var opportunities = dataset.Opportunities.Where(o => o.CustomerId == "CUS-0001").ToList();
        Assert.Equal(2, opportunities.Count);

        var open = Assert.Single(opportunities, o => o.Status == "Open");
        Assert.Equal("PRD-SAV-006M", open.ProductCode);

        // The filter-before-limit fixture: the non-Open record must sort FIRST under the contract
        // ordering, otherwise the regression it guards becomes unobservable.
        var nonOpen = Assert.Single(opportunities, o => o.Status != "Open");
        Assert.True(
            nonOpen.ExpectedCloseDateUtc < open.ExpectedCloseDateUtc,
            "The non-Open opportunity must sort ahead of the Open one for the filter-before-limit test to mean anything.");

        Assert.Equal(2, dataset.Campaigns.Count(c => c.EligibleCustomerIds.Contains("CUS-0001")));
        Assert.Contains(dataset.Campaigns, c => !c.EligibleCustomerIds.Contains("CUS-0001"));
    }

    /// <summary>The periodic-care fallback fixture: a customer with no Open opportunity at all.</summary>
    [Fact]
    public void FallbackCustomer_HasNoOpenOpportunity()
    {
        var dataset = LoadCrmDataset();

        Assert.DoesNotContain(dataset.Opportunities, o => o.CustomerId == "CUS-0004" && o.Status == "Open");
    }
}
