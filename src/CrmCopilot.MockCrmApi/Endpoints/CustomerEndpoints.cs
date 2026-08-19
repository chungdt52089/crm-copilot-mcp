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
