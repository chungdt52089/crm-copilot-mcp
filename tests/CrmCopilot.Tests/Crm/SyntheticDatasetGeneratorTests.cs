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
