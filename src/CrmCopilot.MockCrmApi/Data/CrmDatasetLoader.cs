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

        var customers = ReadJsonFile<List<CustomerDto>>(customersPath);
        var interactions = ReadJsonFile<List<InteractionDto>>(interactionsPath);

        Validate(customers, interactions);

        return new CrmDataset(customers, interactions);
    }

    internal static void Validate(IReadOnlyList<CustomerDto> customers, IReadOnlyList<InteractionDto> interactions)
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

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("CRM dataset failed validation:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
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
