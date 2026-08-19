using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.MockCrmApi.Data;

/// <summary>
/// In-memory, read-only view over the loaded synthetic Customer/Interaction dataset. One
/// instance is registered as a singleton for the process lifetime — P0 does not persist or
/// mutate CRM data.
/// </summary>
internal sealed class CrmDataset
{
    private readonly Dictionary<string, CustomerDto> _customersById;
    private readonly ILookup<string, InteractionDto> _interactionsByCustomerId;

    public CrmDataset(IReadOnlyList<CustomerDto> customers, IReadOnlyList<InteractionDto> interactions)
    {
        Customers = customers;
        Interactions = interactions;
        _customersById = customers.ToDictionary(customer => customer.Id);
        _interactionsByCustomerId = interactions.ToLookup(interaction => interaction.CustomerId);
    }

    public IReadOnlyList<CustomerDto> Customers { get; }

    public IReadOnlyList<InteractionDto> Interactions { get; }

    public CustomerDto? FindById(string customerId) => _customersById.GetValueOrDefault(customerId);

    public bool CustomerExists(string customerId) => _customersById.ContainsKey(customerId);

    public IReadOnlyList<InteractionDto> GetInteractions(string customerId, int limit) =>
        _interactionsByCustomerId[customerId]
            .OrderByDescending(interaction => interaction.OccurredAtUtc)
            .Take(limit)
            .ToList();
}
