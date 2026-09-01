using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.MockCrmApi.Data;
using CrmCopilot.MockCrmApi.Search;

namespace CrmCopilot.MockCrmApi.Endpoints;

/// <summary>
/// Customer/interaction endpoints (docs/06_DATA_AND_MOCK_API_SPEC.md §5-6).
///
/// P0-14 (PD-023) adds the project's one write endpoint: DELETE soft-deletes a customer into
/// <see cref="SoftDeleteRegistry"/>, in memory only. Every read handler consults that registry
/// first, so a deleted customer is uniformly 404 across lookup, search, interactions,
/// opportunities and campaigns — no read path is left still seeing it.
/// </summary>
internal static class CustomerEndpoints
{
    private const string Source = "mock-crm";
    private const int DefaultInteractionLimit = 5;
    private const int MinInteractionLimit = 1;
    private const int MaxInteractionLimit = 20;

    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/customers/{customerId}", GetCustomerById);
        app.MapGet("/api/customers", SearchCustomers);
        app.MapGet("/api/customers/{customerId}/interactions", GetInteractions);
        app.MapGet("/api/customers/{customerId}/opportunities", GetOpportunities);
        app.MapGet("/api/customers/{customerId}/campaigns", GetCampaigns);
        app.MapDelete("/api/customers/{customerId}", DeleteCustomer);

        return app;
    }

    private static IResult GetCustomerById(string customerId, CrmDataset dataset, SoftDeleteRegistry deleted, HttpContext http)
    {
        var customer = deleted.IsDeleted(customerId) ? null : dataset.FindById(customerId);

        return customer is null ? NotFound(http) : Ok(customer, http);
    }

    /// <summary>
    /// P0-14 (PD-023). Soft delete: the id is recorded in <see cref="SoftDeleteRegistry"/> and the
    /// on-disk dataset is never touched.
    ///
    /// The short-circuit order is load-bearing. <see cref="SoftDeleteRegistry.TryDelete"/> only runs
    /// once the customer is known to exist, so an unknown id can never enter the registry; and a
    /// second DELETE finds TryDelete false and answers 404 — the same thing every GET now says about
    /// that id, rather than reporting a success that deleted nothing.
    /// </summary>
    private static IResult DeleteCustomer(string customerId, CrmDataset dataset, SoftDeleteRegistry deleted, HttpContext http)
    {
        if (!dataset.CustomerExists(customerId) || !deleted.TryDelete(customerId))
        {
            return NotFound(http);
        }

        return Results.NoContent();
    }

    private static IResult SearchCustomers(string? query, CrmDataset dataset, SoftDeleteRegistry deleted, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return InvalidArgument(http, "query phải khác rỗng.");
        }

        var result = CustomerSearch.Search(dataset, query, deleted);

        return result.Status switch
        {
            CustomerLookupStatus.Found => Ok(result.Customer!, http),
            CustomerLookupStatus.Ambiguous => Ambiguous(result.Candidates, http),
            _ => NotFound(http),
        };
    }

    private static IResult GetInteractions(string customerId, int? limit, CrmDataset dataset, SoftDeleteRegistry deleted, HttpContext http)
    {
        var effectiveLimit = limit ?? DefaultInteractionLimit;

        if (effectiveLimit is < MinInteractionLimit or > MaxInteractionLimit)
        {
            return InvalidArgument(http, $"limit phải trong khoảng {MinInteractionLimit}-{MaxInteractionLimit}.");
        }

        if (IsMissing(customerId, dataset, deleted))
        {
            return NotFound(http);
        }

        var interactions = dataset.GetInteractions(customerId, effectiveLimit);

        return Results.Json(
            new ApiEnvelope<IReadOnlyList<InteractionDto>>(interactions, http.TraceIdentifier, Source),
            CrmJsonOptions.Default,
            statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// P0-10. <c>status</c> is optional; when present it must be one of
    /// <see cref="OpportunityStatuses.All"/> (case-insensitive) or this is a 400, not a silently
    /// empty result. The normalized value is handed to <see cref="CrmDataset.GetOpportunities"/>,
    /// which owns the filter-before-limit ordering contract (plan Amendment A1).
    /// </summary>
    private static IResult GetOpportunities(string customerId, string? status, int? limit, CrmDataset dataset, SoftDeleteRegistry deleted, HttpContext http)
    {
        var effectiveLimit = limit ?? DefaultInteractionLimit;

        if (effectiveLimit is < MinInteractionLimit or > MaxInteractionLimit)
        {
            return InvalidArgument(http, $"limit phải trong khoảng {MinInteractionLimit}-{MaxInteractionLimit}.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!OpportunityStatuses.TryNormalize(status, out var normalized))
            {
                return InvalidArgument(http, $"status phải là một trong {string.Join(", ", OpportunityStatuses.All)}.");
            }

            normalizedStatus = normalized;
        }

        if (IsMissing(customerId, dataset, deleted))
        {
            return NotFound(http);
        }

        var opportunities = dataset.GetOpportunities(customerId, normalizedStatus, effectiveLimit);

        return Results.Json(
            new ApiEnvelope<IReadOnlyList<OpportunityDto>>(opportunities, http.TraceIdentifier, Source),
            CrmJsonOptions.Default,
            statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// P0-10. Only campaigns this customer is explicitly eligible for — there is deliberately no
    /// "list every campaign" mode at P0-10 (plan D10).
    /// </summary>
    private static IResult GetCampaigns(string customerId, int? limit, CrmDataset dataset, SoftDeleteRegistry deleted, HttpContext http)
    {
        var effectiveLimit = limit ?? DefaultInteractionLimit;

        if (effectiveLimit is < MinInteractionLimit or > MaxInteractionLimit)
        {
            return InvalidArgument(http, $"limit phải trong khoảng {MinInteractionLimit}-{MaxInteractionLimit}.");
        }

        if (IsMissing(customerId, dataset, deleted))
        {
            return NotFound(http);
        }

        var campaigns = dataset.GetCampaigns(customerId, effectiveLimit);

        return Results.Json(
            new ApiEnvelope<IReadOnlyList<CampaignDto>>(campaigns, http.TraceIdentifier, Source),
            CrmJsonOptions.Default,
            statusCode: StatusCodes.Status200OK);
    }

    /// <summary>P0-14: soft-deleted is indistinguishable from absent on every read path — stated
    /// once here rather than re-derived in each customer-scoped handler.</summary>
    private static bool IsMissing(string customerId, CrmDataset dataset, SoftDeleteRegistry deleted) =>
        deleted.IsDeleted(customerId) || !dataset.CustomerExists(customerId);

    private static IResult Ok<T>(T data, HttpContext http) =>
        Results.Json(new ApiEnvelope<T>(data, http.TraceIdentifier, Source), CrmJsonOptions.Default, statusCode: StatusCodes.Status200OK);

    private static IResult Ambiguous(IReadOnlyList<CustomerCandidateDto> candidates, HttpContext http) =>
        Results.Json(
            new ApiEnvelope<IReadOnlyList<CustomerCandidateDto>>(candidates, http.TraceIdentifier, Source),
            CrmJsonOptions.Default,
            statusCode: StatusCodes.Status409Conflict);

    private static IResult NotFound(HttpContext http) =>
        Results.Json(
            new ApiErrorEnvelope(new ApiErrorDetail(ApiErrorCode.NotFound, "Không tìm thấy khách hàng phù hợp.", false), http.TraceIdentifier),
            CrmJsonOptions.Default,
            statusCode: StatusCodes.Status404NotFound);

    private static IResult InvalidArgument(HttpContext http, string message) =>
        Results.Json(
            new ApiErrorEnvelope(new ApiErrorDetail(ApiErrorCode.InvalidArgument, message, false), http.TraceIdentifier),
            CrmJsonOptions.Default,
            statusCode: StatusCodes.Status400BadRequest);
}
