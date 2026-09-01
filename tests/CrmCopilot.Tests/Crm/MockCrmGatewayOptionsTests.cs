using CrmCopilot.Tests.TestSupport;

namespace CrmCopilot.Tests.Crm;

public class MockCrmGatewayOptionsTests
{
    [Fact]
    public void Host_WithNoBaseUrlConfiguredAtAll_FailsToStart()
    {
        // No appsettings.json default exists anymore and nothing injects the key — reproduces a
        // genuinely unconfigured deployment.
        using var factory = McpServerTestHost.CreateWithoutMockCrmApiBaseUrl();

        // ValidateOnStart runs during host startup regardless of whether ICrmGateway is ever
        // resolved — CreateClient() is what triggers that startup.
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Host_WithBlankMockCrmApiBaseUrl_FailsToStart()
    {
        using var factory = McpServerTestHost.CreateWithMockCrmApiBaseUrl("");

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Host_WithValidHttpBaseUrl_StartsSuccessfully()
    {
        // No MockCrmApi needs to actually be running at this address for the host to start —
        // ValidateOnStart only checks URI syntax (Uri.TryCreate), and MockCrmGateway's HttpClient
        // BaseAddress is only set lazily when ICrmGateway is first resolved (nothing does that
        // yet). The host booting successfully here, with nothing listening on port 5100, is
        // itself evidence that no network call happens during startup.
        await using var factory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Host_WithValidHttpsBaseUrl_StartsSuccessfully()
    {
        await using var factory = McpServerTestHost.CreateWithMockCrmApiBaseUrl("https://mock-crm.internal.test");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }
}
