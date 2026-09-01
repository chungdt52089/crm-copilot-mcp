using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;

namespace CrmCopilot.McpServer.Crm;

/// <summary>
/// ICrmGateway implementation calling CrmCopilot.MockCrmApi over HTTP. Full error-mapping
/// contract lives in the P0-02 plan; see the class-level tests for the exhaustive branch list.
/// Not consumed by anything yet — registered so config fail-fast is active ahead of P0-04.
/// </summary>
internal sealed class MockCrmGateway(HttpClient httpClient) : ICrmGateway
{
    public async Task<CustomerLookupResult> FindCustomerAsync(CustomerLookupQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var requestUri = query.CustomerId is not null
            ? $"/api/customers/{Uri.EscapeDataString(query.CustomerId)}"
            : $"/api/customers?query={Uri.EscapeDataString(query.Query!)}";

        using var response = await SendGetAsync(requestUri, cancellationToken).ConfigureAwait(false);

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var customer = await ReadDataAsync<CustomerDto>(response, cancellationToken).ConfigureAwait(false);
                return CustomerLookupResult.Found(customer);

            case HttpStatusCode.NotFound:
                return CustomerLookupResult.NotFound;

            case HttpStatusCode.Conflict:
                var candidates = await ReadDataAsync<IReadOnlyList<CustomerCandidateDto>>(response, cancellationToken).ConfigureAwait(false);
                return CustomerLookupResult.Ambiguous(candidates);

            default:
                throw await BuildUpstreamExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<InteractionDto>> GetInteractionsAsync(string customerId, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        if (limit is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be between 1 and 20.");
        }

        var requestUri = $"/api/customers/{Uri.EscapeDataString(customerId)}/interactions?limit={limit}";
        using var response = await SendGetAsync(requestUri, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return await ReadDataAsync<IReadOnlyList<InteractionDto>>(response, cancellationToken).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var traceId = await TryReadErrorTraceIdAsync(response, cancellationToken).ConfigureAwait(false);
            throw new CrmNotFoundException(customerId, traceId);
        }

        throw await BuildUpstreamExceptionAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpportunityDto>> GetOpportunitiesAsync(
        string customerId, string? status, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        if (limit is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be between 1 and 20.");
        }

        var requestUri = $"/api/customers/{Uri.EscapeDataString(customerId)}/opportunities?limit={limit}";

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Normalized here rather than forwarded verbatim so an unrecognized value fails as a
            // caller error at this boundary, instead of reaching the API as a 400 that
            // BuildUpstreamExceptionAsync would then report as upstream contract drift.
            if (!OpportunityStatuses.TryNormalize(status, out var normalizedStatus))
            {
                throw new ArgumentException($"status must be one of {string.Join("/", OpportunityStatuses.All)}.", nameof(status));
            }

            requestUri += $"&status={Uri.EscapeDataString(normalizedStatus)}";
        }

        return await GetCustomerScopedListAsync<OpportunityDto>(requestUri, customerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CampaignDto>> GetCampaignsAsync(string customerId, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        if (limit is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be between 1 and 20.");
        }

        var requestUri = $"/api/customers/{Uri.EscapeDataString(customerId)}/campaigns?limit={limit}";

        return await GetCustomerScopedListAsync<CampaignDto>(requestUri, customerId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// P0-14 (PD-023). The only write call in this gateway.
    ///
    /// Deliberately accepts <b>204 alone</b> as success. A 200 would mean MockCrmApi started
    /// returning a body for DELETE — contract drift, not a caller error — and
    /// <see cref="BuildUpstreamExceptionAsync"/> reports it as exactly that rather than letting an
    /// unrecognized shape pass for a successful delete.
    /// </summary>
    public async Task DeleteCustomerAsync(string customerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        var requestUri = $"/api/customers/{Uri.EscapeDataString(customerId)}";
        using var response = await SendDeleteAsync(requestUri, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var traceId = await TryReadErrorTraceIdAsync(response, cancellationToken).ConfigureAwait(false);
            throw new CrmNotFoundException(customerId, traceId);
        }

        throw await BuildUpstreamExceptionAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared 200/404/other mapping for the two customer-scoped collection endpoints added in
    /// P0-10 — identical in shape to <see cref="GetInteractionsAsync"/>'s body, which predates it
    /// and is left untouched.
    /// </summary>
    private async Task<IReadOnlyList<T>> GetCustomerScopedListAsync<T>(
        string requestUri, string customerId, CancellationToken cancellationToken)
    {
        using var response = await SendGetAsync(requestUri, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return await ReadDataAsync<IReadOnlyList<T>>(response, cancellationToken).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var traceId = await TryReadErrorTraceIdAsync(response, cancellationToken).ConfigureAwait(false);
            throw new CrmNotFoundException(customerId, traceId);
        }

        throw await BuildUpstreamExceptionAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> SendGetAsync(string requestUri, CancellationToken cancellationToken) =>
        SendAsync(token => httpClient.GetAsync(requestUri, token), cancellationToken);

    /// <summary>P0-14: DELETE shares GET's transport-failure mapping verbatim, so the two verbs can
    /// never drift apart on what counts as retryable.</summary>
    private Task<HttpResponseMessage> SendDeleteAsync(string requestUri, CancellationToken cancellationToken) =>
        SendAsync(token => httpClient.DeleteAsync(requestUri, token), cancellationToken);

    private static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        try
        {
            return await send(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CrmUpstreamException("Failed to reach Mock CRM API.", retryable: true, traceId: null, inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout fired (not the caller's token) — this is transport-level,
            // never surfaced as a caller-cancellation.
            throw new CrmUpstreamException("Mock CRM API request timed out.", retryable: true, traceId: null, inner: ex);
        }
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ApiEnvelope<T>? envelope;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(CrmJsonOptions.Default, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Malformed/unexpected body is never treated as "not found" — it is a distinct,
            // non-retryable upstream contract violation.
            throw new CrmUpstreamException("Mock CRM API returned a response that could not be parsed.", retryable: false, traceId: null, inner: ex);
        }

        if (envelope is null)
        {
            throw new CrmUpstreamException("Mock CRM API returned an empty response body.", retryable: false, traceId: null);
        }

        return envelope.Data;
    }

    private static async Task<CrmUpstreamException> BuildUpstreamExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if ((int)response.StatusCode >= 500)
        {
            try
            {
                var errorEnvelope = await response.Content
                    .ReadFromJsonAsync<ApiErrorEnvelope>(CrmJsonOptions.Default, cancellationToken)
                    .ConfigureAwait(false);

                if (errorEnvelope is not null)
                {
                    return new CrmUpstreamException(
                        $"Mock CRM API returned {(int)response.StatusCode}: {errorEnvelope.Error.Message}",
                        retryable: errorEnvelope.Error.Retryable,
                        traceId: errorEnvelope.TraceId);
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // Fall through: unparseable 5xx body is still treated as retryable (transport/
                // upstream instability), just without a traceId/message from the body.
            }

            return new CrmUpstreamException($"Mock CRM API returned {(int)response.StatusCode} with an unparseable body.", retryable: true, traceId: null);
        }

        // Any status the gateway doesn't otherwise expect (including a 400 the gateway's own
        // pre-validation should have prevented) signals contract drift, not caller error.
        return new CrmUpstreamException($"Mock CRM API returned unexpected status {(int)response.StatusCode}.", retryable: false, traceId: null);
    }

    private static async Task<string?> TryReadErrorTraceIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var errorEnvelope = await response.Content
                .ReadFromJsonAsync<ApiErrorEnvelope>(CrmJsonOptions.Default, cancellationToken)
                .ConfigureAwait(false);
            return errorEnvelope?.TraceId;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
