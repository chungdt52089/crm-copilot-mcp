using CrmCopilot.Contracts.Auth;
using CrmCopilot.Web;
using CrmCopilot.Web.Chat;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CrmCopilot.Tests.TestSupport;

/// <summary>
/// Mirrors McpServerTestHost for CrmCopilot.Web. CrmCopilot.Web.appsettings.json intentionally
/// ships no MCPSERVER_BASE_URL/GEMINI_API_KEY defaults — the real runtime must fail fast when
/// either is missing, same fail-fast convention as McpServer's own options. These values only need
/// to be syntactically valid (ValidateOnStart is a Uri.TryCreate/non-empty check, no network call)
/// — every P0-05 test overrides IGeminiChatClient/IMcpClientProvider via DI, so the real
/// GeminiChatClient/McpClientProvider registered from these values are never actually invoked.
/// </summary>
internal static class WebTestHost
{
    public const string ValidMcpServerBaseUrl = "http://localhost:5090";
    public const string ValidGeminiApiKey = "test-gemini-api-key";

    public static WebApplicationFactory<WebEntryPoint> CreateWithDefaults() =>
        CreateWith(new Dictionary<string, string?>
        {
            [McpClientOptions.BaseUrlConfigKey] = ValidMcpServerBaseUrl,
            [GeminiChatOptions.ApiKeyConfigKey] = ValidGeminiApiKey,
            [McpJwtDefaults.SigningKeyConfigKey] = McpTestTokens.SigningKey,
        });

    private static WebApplicationFactory<WebEntryPoint> CreateWith(Dictionary<string, string?> configuration) =>
        new WebApplicationFactory<WebEntryPoint>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(configuration)));
}
