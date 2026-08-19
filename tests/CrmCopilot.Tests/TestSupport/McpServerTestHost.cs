using CrmCopilot.McpServer;
using CrmCopilot.McpServer.Crm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CrmCopilot.Tests.TestSupport;

/// <summary>
/// CrmCopilot.McpServer.appsettings.json intentionally ships no MOCKCRM_API_BASE_URL default —
/// the real runtime must fail fast when it is not supplied (e.g. via a real environment
/// variable), not silently fall back to localhost. Tests that boot the McpServer host must
/// therefore inject it explicitly, the same way a real deployment would via the environment.
/// This is test-only configuration: injecting a syntactically valid URL here does not perform
/// any network call — MockCrmGatewayOptions validation is a pure Uri.TryCreate(...) check, and
/// the typed HttpClient's BaseAddress is only set lazily when ICrmGateway is first resolved,
/// which nothing in P0-02 does yet.
/// </summary>
internal static class McpServerTestHost
{
    public const string ValidMockCrmApiBaseUrl = "http://localhost:5100";

    /// <summary>Boots McpServer with MOCKCRM_API_BASE_URL injected via in-memory configuration.</summary>
    public static WebApplicationFactory<McpServerEntryPoint> CreateWithMockCrmApiBaseUrl(string baseUrl) =>
        new WebApplicationFactory<McpServerEntryPoint>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [MockCrmGatewayOptions.ConfigKey] = baseUrl,
                })));

    /// <summary>Boots McpServer with no MOCKCRM_API_BASE_URL supplied by any configuration source
    /// at all — reproducing a genuinely unconfigured deployment.</summary>
    public static WebApplicationFactory<McpServerEntryPoint> CreateWithoutMockCrmApiBaseUrl() =>
        new WebApplicationFactory<McpServerEntryPoint>();
}
