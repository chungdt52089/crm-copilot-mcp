using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.MockCrmApi.Data;
using CrmCopilot.MockCrmApi.Data.Generation;

namespace CrmCopilot.Tests.Crm;

public class SyntheticDatasetGeneratorTests
{
    [Fact]
    public void Generate_SameSeed_ProducesIdenticalOutput()
    {
        var options = DatasetGenerationOptions.Default;

        var (customersA, interactionsA) = SyntheticDatasetGenerator.Generate(options);
        var (customersB, interactionsB) = SyntheticDatasetGenerator.Generate(options);

        Assert.Equal(customersA, customersB);
        Assert.Equal(interactionsA, interactionsB);
    }

    /// <summary>
    /// Plan D9. The P0-10 opportunity/campaign generator must not perturb the customer/interaction
    /// stream — those two files are checked in and their SHA-256 hashes are recorded in
    /// docs/CHECKPOINT_STATUS.md §2. Generating the new data before AND after re-running the
    /// original generator proves there is no shared random state between them: the guarantee holds
    /// because SyntheticRelationshipDatasetGenerator uses no Random at all, and this test is what
    /// keeps that true if someone later reaches for one.
    /// </summary>
    [Fact]
    public void GeneratingOpportunitiesAndCampaigns_DoesNotPerturbCustomersOrInteractions()
    {
        var options = DatasetGenerationOptions.Default;

        var (baselineCustomers, baselineInteractions) = SyntheticDatasetGenerator.Generate(options);

        var (customers, interactions) = SyntheticDatasetGenerator.Generate(options);
        var (opportunitiesA, campaignsA) = SyntheticRelationshipDatasetGenerator.Generate(options, customers);
        var (opportunitiesB, campaignsB) = SyntheticRelationshipDatasetGenerator.Generate(options, customers);

        Assert.Equal(baselineCustomers, customers);
        Assert.Equal(baselineInteractions, interactions);

        // Compared as serialized JSON, not as records: CampaignDto has collection-typed members, and
        // record equality compares those by reference. Serialized form is also the thing the
        // guarantee is actually about — these generators write checked-in files.
        Assert.Equal(Serialize(opportunitiesA), Serialize(opportunitiesB));
        Assert.Equal(Serialize(campaignsA), Serialize(campaignsB));

        var (customersAfter, interactionsAfter) = SyntheticDatasetGenerator.Generate(options);
        Assert.Equal(Serialize(baselineCustomers), Serialize(customersAfter));
        Assert.Equal(Serialize(baselineInteractions), Serialize(interactionsAfter));
    }

    private static string Serialize<T>(IReadOnlyList<T> values) =>
        JsonSerializer.Serialize(values, CrmJsonOptions.Indented);

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(50)]
    public void GenerateRelationships_AtVariousScales_ReferencesOnlyExistingCustomers(int customerCount)
    {
        var options = new DatasetGenerationOptions(DatasetGenerationOptions.DefaultSeed, customerCount);
        var (customers, _) = SyntheticDatasetGenerator.Generate(options);

        var (opportunities, campaigns) = SyntheticRelationshipDatasetGenerator.Generate(options, customers);
        var customerIds = customers.Select(customer => customer.Id).ToHashSet();

        Assert.Equal(opportunities.Count, opportunities.Select(o => o.Id).Distinct().Count());
        Assert.All(opportunities, opportunity => Assert.Contains(opportunity.CustomerId, customerIds));
        Assert.All(campaigns, campaign => Assert.All(campaign.EligibleCustomerIds, id => Assert.Contains(id, customerIds)));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(50)]
    [InlineData(200)]
    public void Generate_AtVariousScales_ProducesAValidAndDeterministicDataset(int customerCount)
    {
        var options = new DatasetGenerationOptions(DatasetGenerationOptions.DefaultSeed, customerCount);

        var (customers, interactions) = SyntheticDatasetGenerator.Generate(options);

        Assert.Equal(customerCount, customers.Count);
        Assert.Equal(customerCount, customers.Select(c => c.Id).Distinct().Count());
        Assert.Equal(interactions.Count, interactions.Select(i => i.Id).Distinct().Count());

        var customerIds = customers.Select(c => c.Id).ToHashSet();
        Assert.All(interactions, interaction => Assert.Contains(interaction.CustomerId, customerIds));

        var (customersAgain, interactionsAgain) = SyntheticDatasetGenerator.Generate(options);
        Assert.Equal(customers, customersAgain);
        Assert.Equal(interactions, interactionsAgain);
    }

    [Fact]
    public void Generate_Default_MatchesCheckedInDataset()
    {
        var (generatedCustomers, generatedInteractions) = SyntheticDatasetGenerator.Generate(DatasetGenerationOptions.Default);
        var checkedIn = CrmDatasetLoader.LoadFromAppBaseDirectory();

        Assert.Equal(generatedCustomers, checkedIn.Customers);
        Assert.Equal(generatedInteractions, checkedIn.Interactions);
    }
}
