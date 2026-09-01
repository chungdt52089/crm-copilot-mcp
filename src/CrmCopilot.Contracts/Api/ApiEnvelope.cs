namespace CrmCopilot.Contracts.Api;

/// <summary>
/// Success envelope for Mock CRM API responses (docs/06_DATA_AND_MOCK_API_SPEC.md §6).
/// Also used, as a deliberate exception, for the 409 AMBIGUOUS_MATCH response body
/// (ApiEnvelope&lt;CustomerCandidateDto[]&gt;) — ambiguous match is an actionable result, not a failure.
/// </summary>
public sealed record ApiEnvelope<T>(T Data, string TraceId, string Source);
