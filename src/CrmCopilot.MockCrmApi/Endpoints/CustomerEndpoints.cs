using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.MockCrmApi.Data;
using CrmCopilot.MockCrmApi.Search;

namespace CrmCopilot.MockCrmApi.Endpoints;

/// <summary>
/// Read-only customer/interaction endpoints (docs/06_DATA_AND_MOCK_API_SPEC.md §5-6).
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

        return app;
    }

    private static IResult GetCustomerById(string customerId, CrmDataset dataset, HttpContext http)
    {
        var customer = dataset.FindById(customerId);

        return customer is null ? NotFound(http) : Ok(customer, http);
    }

    private static IResult SearchCustomers(string? query, CrmDataset dataset, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return InvalidArgument(http, "query phải khác rỗng.");
        }

        var result = CustomerSearch.Search(dataset, query);

        return result.Status switch
        {
            CustomerLookupStatus.Found => Ok(result.Customer!, http),
            CustomerLookupStatus.Ambiguous => Ambiguous(result.Candidates, http),
            _ => NotFound(http),
        };
    }

    private static IResult GetInteractions(string customerId, int? limit, CrmDataset dataset, HttpContext http)
    {
        var effectiveLimit = limit ?? DefaultInteractionLimit;

        if (effectiveLimit is < MinInteractionLimit or > MaxInteractionLimit)
        {
            return InvalidArgument(http, $"limit phải trong khoảng {MinInteractionLimit}-{MaxInteractionLimit}.");
        }

        if (!dataset.CustomerExists(customerId))
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
    private static IResult GetOpportunities(string customerId, string? status, int? limit, CrmDataset dataset, HttpContext http)
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

        if (!dataset.CustomerExists(customerId))
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
    private static IResult GetCampaigns(string customerId, int? limit, CrmDataset dataset, HttpContext http)
    {
        var effectiveLimit = limit ?? DefaultInteractionLimit;

        if (effectiveLimit is < MinInteractionLimit or > MaxInteractionLimit)
        {
            return InvalidArgument(http, $"limit phải trong khoảng {MinInteractionLimit}-{MaxInteractionLimit}.");
        }

        if (!dataset.CustomerExists(customerId))
        {
            return NotFound(http);
        }

        var campaigns = dataset.GetCampaigns(customerId, effectiveLimit);

        return Results.Json(
            new ApiEnvelope<IReadOnlyList<CampaignDto>>(campaigns, http.TraceIdentifier, Source),
            CrmJsonOptions.Default,
            statusCode: StatusCodes.Status200OK);
    }

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
