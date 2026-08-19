using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using CrmCopilot.Web;
using CrmCopilot.MockCrmApi;
using CrmCopilot.Tests.TestSupport;

namespace CrmCopilot.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Web_Health_ReturnsOk()
    {
        await using var factory = new WebApplicationFactory<WebEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task McpServer_Health_ReturnsOk()
    {
        // McpServer requires MOCKCRM_API_BASE_URL (fail-fast, no appsettings.json default) —
        // this test injects it the same way a real deployment would via the environment.
        await using var factory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MockCrmApi_Health_ReturnsOk()
    {
        await using var factory = new WebApplicationFactory<MockCrmApiEntryPoint>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
