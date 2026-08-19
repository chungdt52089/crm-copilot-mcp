using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using CrmCopilot.Web;
using CrmCopilot.McpServer;
using CrmCopilot.MockCrmApi;

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
        await using var factory = new WebApplicationFactory<McpServerEntryPoint>();
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
