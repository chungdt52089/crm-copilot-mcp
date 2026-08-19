using CrmCopilot.Contracts.Crm;
using CrmCopilot.MockCrmApi.Data;

namespace CrmCopilot.MockCrmApi.Search;

/// <summary>
/// Search behavior for GET /api/customers?query= (docs/06_DATA_AND_MOCK_API_SPEC.md §7):
/// exact ID match wins over name match; unique normalized full-name match returns that
/// customer; multiple matches return the minimal candidate list (never auto-picked). No fuzzy
/// matching.
/// </summary>
internal static class CustomerSearch
{
    public static CustomerLookupResult Search(CrmDataset dataset, string query)
    {
        var exact = dataset.FindById(query);
        if (exact is not null)
        {
            return CustomerLookupResult.Found(exact);
        }

        var normalizedQuery = CustomerNameNormalizer.Normalize(query);
        var matches = dataset.Customers
            .Where(customer => CustomerNameNormalizer.Normalize(customer.FullName) == normalizedQuery)
            .ToList();

        return matches.Count switch
        {
            0 => CustomerLookupResult.NotFound,
            1 => CustomerLookupResult.Found(matches[0]),
            _ => CustomerLookupResult.Ambiguous(
                matches
                    .Select(customer => new CustomerCandidateDto(customer.Id, customer.FullName, customer.Segment, customer.City))
                    .ToList()),
        };
    }
}
