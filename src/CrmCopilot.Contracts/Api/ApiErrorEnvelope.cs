namespace CrmCopilot.Contracts.Api;

/// <summary>
/// Error envelope for Mock CRM API responses (docs/06_DATA_AND_MOCK_API_SPEC.md §6).
/// Not used for 409 AMBIGUOUS_MATCH — see ApiEnvelope&lt;T&gt; remarks.
/// </summary>
public sealed record ApiErrorEnvelope(ApiErrorDetail Error, string TraceId);

public sealed record ApiErrorDetail(string Code, string Message, bool Retryable);

/// <summary>
/// Error codes returned by CrmCopilot.MockCrmApi (docs/06 §6). AMBIGUOUS_MATCH is
/// intentionally not a member: see ApiEnvelope&lt;T&gt; remarks for the 409 contract.
/// </summary>
public static class ApiErrorCode
{
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string NotFound = "NOT_FOUND";
    public const string InternalError = "INTERNAL_ERROR";
}
