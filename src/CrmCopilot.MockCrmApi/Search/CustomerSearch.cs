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
    /// <summary>
    /// P0-14: <paramref name="deleted"/> is applied BEFORE the match-count switch below, not to the
    /// result afterwards. That ordering is what makes ambiguity collapse correctly — the dataset has
    /// two customers sharing a normalized full name, so soft-deleting one must turn a 409 Ambiguous
    /// into a legitimate unique Found, rather than a stale two-candidate list.
    /// </summary>
    public static CustomerLookupResult Search(CrmDataset dataset, string query, SoftDeleteRegistry deleted)
    {
        var exact = dataset.FindById(query);
        if (exact is not null && !deleted.IsDeleted(exact.Id))
        {
            return CustomerLookupResult.Found(exact);
        }

        var normalizedQuery = CustomerNameNormalizer.Normalize(query);
        var matches = dataset.Customers
            .Where(customer => !deleted.IsDeleted(customer.Id))
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
