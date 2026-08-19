using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.MockCrmApi.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.Crm;

public class InternalErrorHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_WritesStableEnvelope_WithoutExceptionDetails()
    {
        var handler = new InternalErrorHandler(NullLogger<InternalErrorHandler>.Instance);
        var context = new DefaultHttpContext();
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var handled = await handler.TryHandleAsync(
            context, new InvalidOperationException("super secret internal detail"), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        responseBody.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseBody);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("super secret internal detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);

        var envelope = JsonSerializer.Deserialize<ApiErrorEnvelope>(body, CrmJsonOptions.Default);
        Assert.NotNull(envelope);
        Assert.Equal(ApiErrorCode.InternalError, envelope!.Error.Code);
        Assert.False(envelope.Error.Retryable);
    }
}
