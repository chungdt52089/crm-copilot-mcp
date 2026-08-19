namespace CrmCopilot.Contracts.Crm;

public enum CustomerLookupStatus
{
    Found,
    NotFound,
    Ambiguous,
}

public sealed record CustomerLookupResult(
    CustomerLookupStatus Status,
    CustomerDto? Customer,
    IReadOnlyList<CustomerCandidateDto> Candidates)
{
    public static CustomerLookupResult Found(CustomerDto customer) =>
        new(CustomerLookupStatus.Found, customer, []);

    public static readonly CustomerLookupResult NotFound =
        new(CustomerLookupStatus.NotFound, null, []);

    public static CustomerLookupResult Ambiguous(IReadOnlyList<CustomerCandidateDto> candidates) =>
        new(CustomerLookupStatus.Ambiguous, null, candidates);
}
