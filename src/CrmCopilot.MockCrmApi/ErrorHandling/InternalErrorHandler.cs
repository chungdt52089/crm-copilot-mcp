using CrmCopilot.Contracts.Api;
using Microsoft.AspNetCore.Diagnostics;

namespace CrmCopilot.MockCrmApi.ErrorHandling;

/// <summary>
/// Global fallback for unhandled exceptions: logs the real exception server-side, but the HTTP
/// response is always the stable INTERNAL_ERROR envelope with a safe generic message — never
/// exception details or a stack trace (CLAUDE.md §5).
/// </summary>
internal sealed class InternalErrorHandler(ILogger<InternalErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception while processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";

        var envelope = new ApiErrorEnvelope(
            new ApiErrorDetail(ApiErrorCode.InternalError, "Đã xảy ra lỗi không mong muốn.", Retryable: false),
            httpContext.TraceIdentifier);

        await httpContext.Response.WriteAsJsonAsync(envelope, CrmJsonOptions.Default, cancellationToken);

        return true;
    }
}
