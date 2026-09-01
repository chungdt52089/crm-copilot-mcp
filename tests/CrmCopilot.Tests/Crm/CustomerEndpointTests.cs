using System.Net;
using System.Net.Http.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.MockCrmApi;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CrmCopilot.Tests.Crm;

public class CustomerEndpointTests
{
    [Fact]
    public async Task GetCustomerById_Found_ReturnsCustomer()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers/CUS-0001", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CustomerDto>>(CrmJsonOptions.Default, TestContext.Current.CancellationToken);
        Assert.NotNull(envelope);
        Assert.Equal("CUS-0001", envelope!.Data.Id);
        Assert.Equal("mock-crm", envelope.Source);
    }

    [Fact]
    public async Task GetCustomerById_NotFound_ReturnsNotFoundErrorEnvelope()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers/CUS-9999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>(CrmJsonOptions.Default, TestContext.Current.CancellationToken);
        Assert.Equal(ApiErrorCode.NotFound, envelope!.Error.Code);
    }

    [Fact]
    public async Task SearchCustomers_UniqueName_ReturnsSingleCustomer()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/customers?query=" + Uri.EscapeDataString("Nguyễn Minh Anh"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CustomerDto>>(CrmJsonOptions.Default, TestContext.Current.CancellationToken);
        Assert.Equal("CUS-0001", envelope!.Data.Id);
    }

    [Fact]
    public async Task SearchCustomers_ById_ExactIdWinsOverNameMatching()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers?query=CUS-0001", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CustomerDto>>(CrmJsonOptions.Default, TestContext.Current.CancellationToken);
        Assert.Equal("CUS-0001", envelope!.Data.Id);
    }

    [Fact]
    public async Task SearchCustomers_DuplicateName_ReturnsAmbiguousDataEnvelope_NotErrorEnvelope()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/customers?query=" + Uri.EscapeDataString("Trần Thị Hương"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Locked contract: 409 body is ApiEnvelope<CustomerCandidateDto[]>, never ApiErrorEnvelope.
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CustomerCandidateDto>>>(
            CrmJsonOptions.Default, TestContext.Current.CancellationToken);

        Assert.NotNull(envelope);
        Assert.True(envelope!.Data.Count >= 2);
        Assert.Contains(envelope.Data, candidate => candidate.Id == "CUS-0002");
        Assert.Contains(envelope.Data, candidate => candidate.Id == "CUS-0003");
    }

    [Fact]
    public async Task SearchCustomers_UnknownName_ReturnsNotFound()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/customers?query=" + Uri.EscapeDataString("Không Tồn Tại Trong Dataset"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SearchCustomers_BlankQuery_ReturnsInvalidArgument()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/customers?query=%20", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>(CrmJsonOptions.Default, TestContext.Current.CancellationToken);
        Assert.Equal(ApiErrorCode.InvalidArgument, envelope!.Error.Code);
    }

    /// <summary>
    /// P0-14 (PD-023). Covers the whole soft-delete contract in one pass: the delete succeeds with
    /// 204, the customer then disappears from EVERY read path (lookup, search, interactions,
    /// opportunities, campaigns), a repeat delete is 404 rather than a second success, and — the
    /// part that makes it a *soft* delete — a fresh host still sees the customer.
    ///
    /// That last assertion is the in-test stand-in for "restart MockCrmApi": SoftDeleteRegistry is
    /// registered per-host, so a new WebApplicationFactory is a new registry over the same on-disk
    /// dataset. If anything had been written to data/crm/customers.json, it would fail here.
    /// </summary>
    [Fact]
    public async Task DeleteCustomer_SoftDeletes_AndAFreshHostStillSeesTheCustomer()
    {
        await using (var factory = new WebApplicationFactory<MockCrmApiEntryPoint>())
        {
            using var client = factory.CreateClient();
            var ct = TestContext.Current.CancellationToken;

            var deleteResponse = await client.DeleteAsync("/api/customers/CUS-0001", ct);
            Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

            foreach (var route in new[]
                     {
                         "/api/customers/CUS-0001",
                         "/api/customers/CUS-0001/interactions",
                         "/api/customers/CUS-0001/opportunities",
                         "/api/customers/CUS-0001/campaigns",
                         "/api/customers?query=CUS-0001",
                     })
            {
                var readResponse = await client.GetAsync(route, ct);
                Assert.Equal(HttpStatusCode.NotFound, readResponse.StatusCode);
            }

            // Already deleted is reported exactly as never existed — the same thing every read above
            // now says about this id — instead of a success that deleted nothing.
            var repeatResponse = await client.DeleteAsync("/api/customers/CUS-0001", ct);
            Assert.Equal(HttpStatusCode.NotFound, repeatResponse.StatusCode);

            var unknownResponse = await client.DeleteAsync("/api/customers/CUS-9999", ct);
            Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        }

        await using var freshFactory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var freshClient = freshFactory.CreateClient();

        var afterRestart = await freshClient.GetAsync("/api/customers/CUS-0001", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, afterRestart.StatusCode);
        var restored = await afterRestart.Content.ReadFromJsonAsync<ApiEnvelope<CustomerDto>>(
            CrmJsonOptions.Default, TestContext.Current.CancellationToken);
        Assert.Equal("CUS-0001", restored!.Data.Id);
    }
}
