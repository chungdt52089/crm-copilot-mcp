using CrmCopilot.McpServer;
using CrmCopilot.McpServer.Crm;
using CrmCopilot.Contracts.Auth;
using CrmCopilot.McpServer.Knowledge;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CrmCopilot.Tests.TestSupport;

/// <summary>
/// CrmCopilot.McpServer.appsettings.json intentionally ships no MOCKCRM_API_BASE_URL/
/// GEMINI_API_KEY/CHROMA_BASE_URL defaults — the real runtime must fail fast when any is not
/// supplied, not silently fall back. Tests that boot the McpServer host must therefore inject
/// them explicitly, the same way a real deployment would via the environment. This is test-only
/// configuration: injecting syntactically valid values here does not perform any network call —
/// every option's validation is a pure Uri.TryCreate/non-empty-string check, and the typed
/// HttpClients' BaseAddress is only set lazily when a gateway/store is first resolved, which
/// nothing in P0-02/P0-03 does yet.
///
/// AddKnowledgeRetrieval's ValidateOnStart runs for every host boot regardless of which option a
/// given test is actually exercising, so any "this should start successfully" helper must supply
/// valid values for all three options — see CreateWithMockCrmApiBaseUrl below, extended for
/// P0-03 without changing its existing P0-02 callers' behavior.
///
/// Ambient-override pitfall (found during P0-03 live-credential verification, do not "simplify"
/// this back): WebApplicationFactory boots the real Program.cs, which calls
/// WebApplication.CreateBuilder(args) — this registers AddEnvironmentVariables() as a
/// configuration source *before* WithWebHostBuilder's ConfigureAppConfiguration callback appends
/// AddInMemoryCollection(...) on top. ConfigurationRoot's indexer walks providers in reverse
/// registration order and returns the value from the first provider whose TryGet returns true —
/// regardless of whether that value is null. If the in-memory dictionary simply omits a key
/// (rather than explicitly setting it), its TryGet returns false for that key, so the search
/// falls through to the environment-variables provider — meaning a real ambient GEMINI_API_KEY
/// in the parent shell (e.g. one exported to run the live acceptance CLI) silently leaks into a
/// test that intended to simulate "this key is missing", making ValidateOnStart pass when the
/// test expects it to fail. The fix is an *explicit* `null` entry (a "tombstone") in the
/// in-memory dictionary for a "without X" case: MemoryConfigurationProvider stores the entry
/// as-is (including a literal null value), so its TryGet returns true with value=null for that
/// key — this correctly short-circuits ConfigurationRoot's search before it ever reaches the
/// environment-variables provider, deterministically masking any ambient value without ever
/// reading or mutating the real process environment. A "blank" case (CreateWithBlank*) uses an
/// explicit `""` instead — same masking mechanism, but a genuinely different configured state
/// (present-but-empty vs. absent), so the two categories of test stay distinguishable.
/// </summary>
internal static class McpServerTestHost
{
    public const string ValidMockCrmApiBaseUrl = "http://localhost:5100";
    public const string ValidGeminiApiKey = "test-gemini-api-key";
    public const string ValidChromaBaseUrl = "http://localhost:8000";

    /// <summary>P0-13: /mcp now requires a validated bearer token, so every host that is expected
    /// to START must be given a signing key. Shared with McpTestTokens so the tokens tests present
    /// verify against this same key.</summary>
    public const string ValidMcpJwtSigningKey = McpTestTokens.SigningKey;

    /// <summary>Boots McpServer with MOCKCRM_API_BASE_URL injected as given, plus valid default
    /// GEMINI_API_KEY/CHROMA_BASE_URL so existing P0-02 tests keep passing under P0-03's added
    /// ValidateOnStart options.</summary>
    public static WebApplicationFactory<McpServerEntryPoint> CreateWithMockCrmApiBaseUrl(string baseUrl) =>
        CreateWith(new Dictionary<string, string?>
        {
            [MockCrmGatewayOptions.ConfigKey] = baseUrl,
            [GeminiEmbeddingOptions.ApiKeyConfigKey] = ValidGeminiApiKey,
            [ChromaOptions.BaseUrlConfigKey] = ValidChromaBaseUrl,
            [McpJwtDefaults.SigningKeyConfigKey] = ValidMcpJwtSigningKey,
        });

    /// <summary>Boots McpServer with no configuration supplied by any source at all —
    /// reproducing a genuinely unconfigured deployment.</summary>
    public static WebApplicationFactory<McpServerEntryPoint> CreateWithoutMockCrmApiBaseUrl() =>
        new WebApplicationFactory<McpServerEntryPoint>();

    public static WebApplicationFactory<McpServerEntryPoint> CreateWithoutGeminiApiKey() =>
        CreateWith(new Dictionary<string, string?>
        {
            [MockCrmGatewayOptions.ConfigKey] = ValidMockCrmApiBaseUrl,
            [GeminiEmbeddingOptions.ApiKeyConfigKey] = null, // tombstone, not omission — see class doc
            [ChromaOptions.BaseUrlConfigKey] = ValidChromaBaseUrl,
            [McpJwtDefaults.SigningKeyConfigKey] = ValidMcpJwtSigningKey,
        });

    public static WebApplicationFactory<McpServerEntryPoint> CreateWithBlankGeminiApiKey() =>
        CreateWith(new Dictionary<string, string?>
        {
            [MockCrmGatewayOptions.ConfigKey] = ValidMockCrmApiBaseUrl,
            [GeminiEmbeddingOptions.ApiKeyConfigKey] = "",
            [ChromaOptions.BaseUrlConfigKey] = ValidChromaBaseUrl,
            [McpJwtDefaults.SigningKeyConfigKey] = ValidMcpJwtSigningKey,
        });

    public static WebApplicationFactory<McpServerEntryPoint> CreateWithoutChromaBaseUrl() =>
        CreateWith(new Dictionary<string, string?>
        {
            [MockCrmGatewayOptions.ConfigKey] = ValidMockCrmApiBaseUrl,
            [GeminiEmbeddingOptions.ApiKeyConfigKey] = ValidGeminiApiKey,
            [ChromaOptions.BaseUrlConfigKey] = null, // tombstone, not omission — see class doc
            [McpJwtDefaults.SigningKeyConfigKey] = ValidMcpJwtSigningKey,
        });

    public static WebApplicationFactory<McpServerEntryPoint> CreateWithBlankChromaBaseUrl() =>
        CreateWith(new Dictionary<string, string?>
        {
            [MockCrmGatewayOptions.ConfigKey] = ValidMockCrmApiBaseUrl,
            [GeminiEmbeddingOptions.ApiKeyConfigKey] = ValidGeminiApiKey,
            [ChromaOptions.BaseUrlConfigKey] = "",
            [McpJwtDefaults.SigningKeyConfigKey] = ValidMcpJwtSigningKey,
        });

    /// <summary>Regression coverage for the ambient-override bug (see class doc): stacks a
    /// lower-priority in-memory source standing in for a real ambient environment variable —
    /// never Environment.SetEnvironmentVariable, so this is safe under xUnit's parallel test
    /// execution and never mutates process-wide state — under a higher-priority source that
    /// null-tombstones <paramref name="configKey"/> while supplying valid values for the other
    /// two required options, so only the key under test is exercised. Used for both
    /// GEMINI_API_KEY and CHROMA_BASE_URL via a shared [Theory].</summary>
    public static WebApplicationFactory<McpServerEntryPoint> CreateWithSimulatedAmbientValueMaskedByNullTombstone(
        string configKey, string simulatedAmbientValue) =>
        new WebApplicationFactory<McpServerEntryPoint>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?> { [configKey] = simulatedAmbientValue });

                var overrides = new Dictionary<string, string?>
                {
                    [MockCrmGatewayOptions.ConfigKey] = ValidMockCrmApiBaseUrl,
                    [GeminiEmbeddingOptions.ApiKeyConfigKey] = ValidGeminiApiKey,
                    [ChromaOptions.BaseUrlConfigKey] = ValidChromaBaseUrl,
            [McpJwtDefaults.SigningKeyConfigKey] = ValidMcpJwtSigningKey,
                };
                overrides[configKey] = null; // tombstone — masks the simulated-ambient layer above
                configurationBuilder.AddInMemoryCollection(overrides);
            }));

    private static WebApplicationFactory<McpServerEntryPoint> CreateWith(Dictionary<string, string?> configuration) =>
        new WebApplicationFactory<McpServerEntryPoint>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                configurationBuilder.AddInMemoryCollection(configuration)));
}
