using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.MockCrmApi.Data;

/// <summary>
/// Loads and validates data/crm/*.json at startup — fails fast (throws) on any structural
/// problem rather than letting the API run against unusable data (docs/06 §9). Path is resolved
/// via AppContext.BaseDirectory, not the process's current working directory, so behavior is
/// identical under `dotnet run`, Visual Studio, and WebApplicationFactory-hosted tests (the
/// files are copied into every consuming project's own build output — see
/// CrmCopilot.MockCrmApi.csproj's CopyToOutputDirectory item).
/// </summary>
internal static class CrmDatasetLoader
{
    public static CrmDataset LoadFromAppBaseDirectory() =>
        LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "crm"));

    public static CrmDataset LoadFromDirectory(string directory)
    {
        var customersPath = Path.Combine(directory, "customers.json");
        var interactionsPath = Path.Combine(directory, "interactions.json");
        var opportunitiesPath = Path.Combine(directory, "opportunities.json");
        var campaignsPath = Path.Combine(directory, "campaigns.json");

        var customers = ReadJsonFile<List<CustomerDto>>(customersPath);
        var interactions = ReadJsonFile<List<InteractionDto>>(interactionsPath);
        var opportunities = ReadJsonFile<List<OpportunityDto>>(opportunitiesPath);
        var campaigns = ReadJsonFile<List<CampaignDto>>(campaignsPath);

        Validate(customers, interactions, opportunities, campaigns);

        return new CrmDataset(customers, interactions, opportunities, campaigns);
    }

    internal static void Validate(
        IReadOnlyList<CustomerDto> customers,
        IReadOnlyList<InteractionDto> interactions,
        IReadOnlyList<OpportunityDto> opportunities,
        IReadOnlyList<CampaignDto> campaigns)
    {
        var errors = new List<string>();
        var customerIds = new HashSet<string>();

        foreach (var customer in customers)
        {
            if (!customer.Id.StartsWith("CUS-", StringComparison.Ordinal))
            {
                errors.Add($"Customer id '{customer.Id}' does not use the CUS- prefix.");
            }

            if (!customerIds.Add(customer.Id))
            {
                errors.Add($"Duplicate customer id '{customer.Id}'.");
            }

            if (!customer.Synthetic)
            {
                errors.Add($"Customer '{customer.Id}' is not marked synthetic.");
            }

            if (customer.UpdatedAtUtc.Kind != DateTimeKind.Utc)
            {
                errors.Add($"Customer '{customer.Id}' UpdatedAtUtc is not UTC.");
            }
        }

        var interactionIds = new HashSet<string>();

        foreach (var interaction in interactions)
        {
            if (!interaction.Id.StartsWith("INT-", StringComparison.Ordinal))
            {
                errors.Add($"Interaction id '{interaction.Id}' does not use the INT- prefix.");
            }

            if (!interactionIds.Add(interaction.Id))
            {
                errors.Add($"Duplicate interaction id '{interaction.Id}'.");
            }

            if (!interaction.Synthetic)
            {
                errors.Add($"Interaction '{interaction.Id}' is not marked synthetic.");
            }

            if (interaction.OccurredAtUtc.Kind != DateTimeKind.Utc)
            {
                errors.Add($"Interaction '{interaction.Id}' OccurredAtUtc is not UTC.");
            }

            if (!customerIds.Contains(interaction.CustomerId))
            {
                errors.Add($"Interaction '{interaction.Id}' references unknown customer '{interaction.CustomerId}'.");
            }
        }

        ValidateOpportunities(opportunities, customerIds, errors);
        ValidateCampaigns(campaigns, customerIds, errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("CRM dataset failed validation:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    private static void ValidateOpportunities(
        IReadOnlyList<OpportunityDto> opportunities, HashSet<string> customerIds, List<string> errors)
    {
        var opportunityIds = new HashSet<string>();

        foreach (var opportunity in opportunities)
        {
            if (!opportunity.Id.StartsWith("OPP-", StringComparison.Ordinal))
            {
                errors.Add($"Opportunity id '{opportunity.Id}' does not use the OPP- prefix.");
            }

            if (!opportunityIds.Add(opportunity.Id))
            {
                errors.Add($"Duplicate opportunity id '{opportunity.Id}'.");
            }

            if (!opportunity.Synthetic)
            {
                errors.Add($"Opportunity '{opportunity.Id}' is not marked synthetic.");
            }

            if (opportunity.ExpectedCloseDateUtc.Kind != DateTimeKind.Utc)
            {
                errors.Add($"Opportunity '{opportunity.Id}' ExpectedCloseDateUtc is not UTC.");
            }

            if (!customerIds.Contains(opportunity.CustomerId))
            {
                errors.Add($"Opportunity '{opportunity.Id}' references unknown customer '{opportunity.CustomerId}'.");
            }

            // Ordinal, not OrdinalIgnoreCase: the checked-in dataset is generated with the canonical
            // casing, so anything else here is dataset corruption rather than caller leniency.
            if (!OpportunityStatuses.All.Contains(opportunity.Status, StringComparer.Ordinal))
            {
                errors.Add(
                    $"Opportunity '{opportunity.Id}' has status '{opportunity.Status}', which is not one of " +
                    $"{string.Join("/", OpportunityStatuses.All)}.");
            }

            // Format only — see ProductCodeFormat's remarks and plan D17 for why existence against
            // data/knowledge/products.json is a test-level invariant, not a runtime one here.
            if (!ProductCodeFormat.IsWellFormed(opportunity.ProductCode))
            {
                errors.Add($"Opportunity '{opportunity.Id}' has a malformed productCode '{opportunity.ProductCode}'.");
            }

            if (opportunity.AmountVnd < 0)
            {
                errors.Add($"Opportunity '{opportunity.Id}' has a negative amountVnd.");
            }
        }
    }

    private static void ValidateCampaigns(
        IReadOnlyList<CampaignDto> campaigns, HashSet<string> customerIds, List<string> errors)
    {
        var campaignIds = new HashSet<string>();

        foreach (var campaign in campaigns)
        {
            if (!campaign.Id.StartsWith("CMP-", StringComparison.Ordinal))
            {
                errors.Add($"Campaign id '{campaign.Id}' does not use the CMP- prefix.");
            }

            if (!campaignIds.Add(campaign.Id))
            {
                errors.Add($"Duplicate campaign id '{campaign.Id}'.");
            }

            if (!campaign.Synthetic)
            {
                errors.Add($"Campaign '{campaign.Id}' is not marked synthetic.");
            }

            if (campaign.StartDateUtc.Kind != DateTimeKind.Utc)
            {
                errors.Add($"Campaign '{campaign.Id}' StartDateUtc is not UTC.");
            }

            if (campaign.EndDateUtc.Kind != DateTimeKind.Utc)
            {
                errors.Add($"Campaign '{campaign.Id}' EndDateUtc is not UTC.");
            }

            if (campaign.EndDateUtc < campaign.StartDateUtc)
            {
                errors.Add($"Campaign '{campaign.Id}' ends before it starts.");
            }

            if (campaign.ProductCodes.Count == 0)
            {
                errors.Add($"Campaign '{campaign.Id}' has no productCodes.");
            }

            foreach (var productCode in campaign.ProductCodes)
            {
                if (!ProductCodeFormat.IsWellFormed(productCode))
                {
                    errors.Add($"Campaign '{campaign.Id}' has a malformed productCode '{productCode}'.");
                }
            }

            // The eligibility list is the whole point of the campaign record (plan D10) — an unknown
            // id here would silently make a customer un-targetable rather than fail loudly.
            foreach (var customerId in campaign.EligibleCustomerIds)
            {
                if (!customerIds.Contains(customerId))
                {
                    errors.Add($"Campaign '{campaign.Id}' references unknown customer '{customerId}'.");
                }
            }
        }
    }

    private static T ReadJsonFile<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"CRM dataset file not found: {path}");
        }

        var json = File.ReadAllText(path);

        try
        {
            return JsonSerializer.Deserialize<T>(json, CrmJsonOptions.Default)
                ?? throw new InvalidOperationException($"CRM dataset file '{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"CRM dataset file '{path}' is not valid JSON.", ex);
        }
    }
}
