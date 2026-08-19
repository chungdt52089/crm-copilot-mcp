using System.Net;
using System.Net.Http.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.MockCrmApi;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CrmCopilot.Tests.Crm;

public class InteractionEndpointTests
{
    [Fact]
    public async Task GetInteractions_KnownCustomer_ReturnsNewestFirst()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers/CUS-0001/interactions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<InteractionDto>>>(
            CrmJsonOptions.Default, TestContext.Current.CancellationToken);

        Assert.NotNull(envelope);
        Assert.True(envelope!.Data.Count >= 2);
        Assert.All(envelope.Data, interaction => Assert.Equal("CUS-0001", interaction.CustomerId));

        var timestamps = envelope.Data.Select(i => i.OccurredAtUtc).ToList();
        Assert.Equal(timestamps.OrderByDescending(t => t), timestamps);
    }

    [Fact]
    public async Task GetInteractions_ZeroInteractionCustomer_ReturnsEmptyArray_NotNotFound()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers/CUS-0004/interactions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<InteractionDto>>>(
            CrmJsonOptions.Default, TestContext.Current.CancellationToken);

        Assert.NotNull(envelope);
        Assert.Empty(envelope!.Data);
    }

    [Fact]
    public async Task GetInteractions_UnknownCustomer_ReturnsNotFound()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers/CUS-9999/interactions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task GetInteractions_LimitOutOfRange_ReturnsInvalidArgument(int limit)
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/customers/CUS-0001/interactions?limit={limit}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>(CrmJsonOptions.Default, TestContext.Current.CancellationToken);
        Assert.Equal(ApiErrorCode.InvalidArgument, envelope!.Error.Code);
    }

    [Fact]
    public async Task GetInteractions_LimitIsRespected()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers/CUS-0001/interactions?limit=1", TestContext.Current.CancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<InteractionDto>>>(
            CrmJsonOptions.Default, TestContext.Current.CancellationToken);
        Assert.Single(envelope!.Data);
    }
}
