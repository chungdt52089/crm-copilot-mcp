using CrmCopilot.MockCrmApi.Data;

namespace CrmCopilot.Tests.Crm;

public class DatasetValidationTests
{
    private static CrmDataset LoadDataset() => CrmDatasetLoader.LoadFromAppBaseDirectory();

    [Fact]
    public void Dataset_LoadsSuccessfully()
    {
        var dataset = LoadDataset();

        Assert.NotEmpty(dataset.Customers);
        Assert.NotEmpty(dataset.Interactions);
    }

    [Fact]
    public void AllCustomers_HaveUniqueIds()
    {
        var ids = LoadDataset().Customers.Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AllInteractions_HaveUniqueIds()
    {
        var ids = LoadDataset().Interactions.Select(i => i.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AllInteractions_ReferenceExistingCustomer()
    {
        var dataset = LoadDataset();
        var customerIds = dataset.Customers.Select(c => c.Id).ToHashSet();

        Assert.All(dataset.Interactions, interaction => Assert.Contains(interaction.CustomerId, customerIds));
    }

    [Fact]
    public void AllRecords_AreMarkedSynthetic()
    {
        var dataset = LoadDataset();

        Assert.All(dataset.Customers, customer => Assert.True(customer.Synthetic));
        Assert.All(dataset.Interactions, interaction => Assert.True(interaction.Synthetic));
    }

    [Fact]
    public void AllCustomerEmails_UseSyntheticDomain()
    {
        Assert.All(LoadDataset().Customers, customer => Assert.EndsWith("@example.test", customer.Email));
    }

    [Fact]
    public void AllTimestamps_AreUtc()
    {
        var dataset = LoadDataset();

        Assert.All(dataset.Customers, customer => Assert.Equal(DateTimeKind.Utc, customer.UpdatedAtUtc.Kind));
        Assert.All(dataset.Interactions, interaction => Assert.Equal(DateTimeKind.Utc, interaction.OccurredAtUtc.Kind));
    }

    [Fact]
    public void CanonicalCustomer_MatchesDocs06Scenario()
    {
        var canonical = LoadDataset().FindById("CUS-0001");

        Assert.NotNull(canonical);
        Assert.Equal("Nguyễn Minh Anh", canonical!.FullName);
        Assert.Equal("minh.anh@example.test", canonical.Email);
        Assert.Equal("0900000001", canonical.Phone);
        Assert.Equal("000000000001", canonical.AccountReference);
        Assert.Equal("Priority", canonical.Segment);
        Assert.Equal("Hà Nội", canonical.City);
    }

    [Fact]
    public void CanonicalCustomer_NewestInteractionIsSavingsFollowUp()
    {
        var dataset = LoadDataset();
        var interactions = dataset.GetInteractions("CUS-0001", limit: 20);

        Assert.True(interactions.Count >= 2);
        Assert.Contains("kỳ hạn 6 tháng", interactions[0].Summary);
    }

    [Fact]
    public void AtLeastOneCustomer_HasZeroInteractions()
    {
        var dataset = LoadDataset();

        Assert.Contains(dataset.Customers, customer => dataset.GetInteractions(customer.Id, limit: 20).Count == 0);
    }

    [Fact]
    public void AtLeastTwoCustomers_ShareANormalizedFullName()
    {
        var duplicateGroups = LoadDataset().Customers
            .GroupBy(customer => customer.FullName.Trim().ToUpperInvariant())
            .Where(group => group.Count() > 1)
            .ToList();

        Assert.NotEmpty(duplicateGroups);
    }

    [Fact]
    public void NoRecord_ContainsObviousSecretPattern()
    {
        var dataset = LoadDataset();
        var haystacks = dataset.Customers
            .Select(customer => customer.Email + customer.Phone + customer.AccountReference)
            .Concat(dataset.Interactions.Select(interaction => interaction.Summary + interaction.NextAction));

        Assert.All(haystacks, text => Assert.DoesNotContain("sk-", text, StringComparison.OrdinalIgnoreCase));
    }
}
