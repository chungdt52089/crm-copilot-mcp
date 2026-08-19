namespace CrmCopilot.Contracts.Crm;

/// <summary>
/// Exactly one of <see cref="CustomerId"/> or <see cref="Query"/> is populated — enforced by
/// construction via <see cref="ById"/>/<see cref="ByQuery"/>, not by a post-hoc check, so an
/// invalid instance can never exist.
/// </summary>
public sealed record CustomerLookupQuery
{
    public string? CustomerId { get; }
    public string? Query { get; }

    private CustomerLookupQuery(string? customerId, string? query)
    {
        CustomerId = customerId;
        Query = query;
    }

    public static CustomerLookupQuery ById(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new ArgumentException("customerId must not be blank.", nameof(customerId));
        }

        return new CustomerLookupQuery(customerId, null);
    }

    public static CustomerLookupQuery ByQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query must not be blank.", nameof(query));
        }

        return new CustomerLookupQuery(null, query);
    }
}
