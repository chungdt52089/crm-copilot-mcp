using System.Net;
using System.Text;
using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.McpServer.Crm;
using CrmCopilot.MockCrmApi;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CrmCopilot.Tests.Crm;

/// <summary>
/// Exhaustive MockCrmGateway error-mapping coverage per the P0-02 plan's mapping tables:
/// found/not-found/ambiguous/empty use the real Mock CRM API via WebApplicationFactory; 5xx,
/// transport failures, and malformed bodies use a stub HttpMessageHandler since the real API
/// has no way to deliberately produce those conditions.
/// </summary>
public class MockCrmGatewayTests
{
    // --- real Mock CRM API: happy paths + domain outcomes ---

    [Fact]
    public async Task FindCustomerAsync_Found_MapsToFoundStatus()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();
        var gateway = new MockCrmGateway(client);

        var result = await gateway.FindCustomerAsync(CustomerLookupQuery.ById("CUS-0001"), TestContext.Current.CancellationToken);

        Assert.Equal(CustomerLookupStatus.Found, result.Status);
        Assert.Equal("CUS-0001", result.Customer!.Id);
    }

    [Fact]
    public async Task FindCustomerAsync_NotFound_MapsToNotFoundStatus()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();
        var gateway = new MockCrmGateway(client);

        var result = await gateway.FindCustomerAsync(CustomerLookupQuery.ById("CUS-9999"), TestContext.Current.CancellationToken);

        Assert.Equal(CustomerLookupStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task FindCustomerAsync_Ambiguous_MapsToAmbiguousStatusWithCandidates()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();
        var gateway = new MockCrmGateway(client);

        var result = await gateway.FindCustomerAsync(CustomerLookupQuery.ByQuery("Trần Thị Hương"), TestContext.Current.CancellationToken);

        Assert.Equal(CustomerLookupStatus.Ambiguous, result.Status);
        Assert.True(result.Candidates.Count >= 2);
    }

    [Fact]
    public async Task GetInteractionsAsync_EmptyForZeroInteractionCustomer_ReturnsEmptyList_NotException()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();
        var gateway = new MockCrmGateway(client);

        var interactions = await gateway.GetInteractionsAsync("CUS-0004", 5, TestContext.Current.CancellationToken);

        Assert.Empty(interactions);
    }

    [Fact]
    public async Task GetInteractionsAsync_NotFound_ThrowsCrmNotFoundException_ViaRealApi()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();
        var gateway = new MockCrmGateway(client);

        var exception = await Assert.ThrowsAsync<CrmNotFoundException>(
            () => gateway.GetInteractionsAsync("CUS-9999", 5, TestContext.Current.CancellationToken));

        Assert.Equal("CUS-9999", exception.CustomerId);
    }

    // --- pre-flight validation: no network call needed ---

    [Fact]
    public async Task GetInteractionsAsync_LimitOutOfRange_ThrowsWithoutNetworkCall()
    {
        using var client = new HttpClient(new ThrowingHandler(new InvalidOperationException("must not be called")))
        {
            BaseAddress = new Uri("http://localhost"),
        };
        var gateway = new MockCrmGateway(client);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => gateway.GetInteractionsAsync("CUS-0001", 21, TestContext.Current.CancellationToken));
    }

    // --- stub handler: upstream failure / transport / malformed body mapping ---

    [Fact]
    public async Task FindCustomerAsync_ServerErrorWithParseableBody_UsesRetryableFlagFromBody()
    {
        var errorJson = JsonSerializer.Serialize(
            new ApiErrorEnvelope(new ApiErrorDetail("UPSTREAM_UNAVAILABLE", "tạm thời không khả dụng", Retryable: true), "trace-1"),
            CrmJsonOptions.Default);
        using var client = CreateStubClient(HttpStatusCode.ServiceUnavailable, errorJson);
        var gateway = new MockCrmGateway(client);

        var exception = await Assert.ThrowsAsync<CrmUpstreamException>(
            () => gateway.FindCustomerAsync(CustomerLookupQuery.ById("CUS-0001"), TestContext.Current.CancellationToken));

        Assert.True(exception.Retryable);
        Assert.Equal("trace-1", exception.TraceId);
    }

    [Fact]
    public async Task FindCustomerAsync_ServerErrorWithUnparseableBody_IsRetryable()
    {
        using var client = CreateStubClient(HttpStatusCode.InternalServerError, "not json at all");
        var gateway = new MockCrmGateway(client);

        var exception = await Assert.ThrowsAsync<CrmUpstreamException>(
            () => gateway.FindCustomerAsync(CustomerLookupQuery.ById("CUS-0001"), TestContext.Current.CancellationToken));

        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task FindCustomerAsync_MalformedOkBody_IsNotRetryable_NeverTreatedAsNotFound()
    {
        using var client = CreateStubClient(HttpStatusCode.OK, "{ not valid json");
        var gateway = new MockCrmGateway(client);

        var exception = await Assert.ThrowsAsync<CrmUpstreamException>(
            () => gateway.FindCustomerAsync(CustomerLookupQuery.ById("CUS-0001"), TestContext.Current.CancellationToken));

        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task FindCustomerAsync_UnexpectedStatusCode_IsNotRetryable()
    {
        using var client = CreateStubClient(HttpStatusCode.MovedPermanently, content: null);
        var gateway = new MockCrmGateway(client);

        var exception = await Assert.ThrowsAsync<CrmUpstreamException>(
            () => gateway.FindCustomerAsync(CustomerLookupQuery.ById("CUS-0001"), TestContext.Current.CancellationToken));

        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task FindCustomerAsync_TransportFailure_ThrowsRetryableUpstreamException()
    {
        using var client = new HttpClient(new ThrowingHandler(new HttpRequestException("connection refused")))
        {
            BaseAddress = new Uri("http://localhost"),
        };
        var gateway = new MockCrmGateway(client);

        var exception = await Assert.ThrowsAsync<CrmUpstreamException>(
            () => gateway.FindCustomerAsync(CustomerLookupQuery.ById("CUS-0001"), TestContext.Current.CancellationToken));

        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task GetInteractionsAsync_MalformedOkBody_IsNotRetryable_NeverEmptyList()
    {
        using var client = CreateStubClient(HttpStatusCode.OK, "{ not valid json");
        var gateway = new MockCrmGateway(client);

        var exception = await Assert.ThrowsAsync<CrmUpstreamException>(
            () => gateway.GetInteractionsAsync("CUS-0001", 5, TestContext.Current.CancellationToken));

        Assert.False(exception.Retryable);
    }

    private static HttpClient CreateStubClient(HttpStatusCode statusCode, string? content) =>
        new(new StubHandler(statusCode, content)) { BaseAddress = new Uri("http://localhost") };

    private sealed class StubHandler(HttpStatusCode statusCode, string? content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (content is not null)
            {
                response.Content = new StringContent(content, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }
}
