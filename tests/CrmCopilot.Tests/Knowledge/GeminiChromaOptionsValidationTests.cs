using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.Tests.TestSupport;

namespace CrmCopilot.Tests.Knowledge;

/// <summary>
/// Mirrors CrmCopilot.Tests.Crm.MockCrmGatewayOptionsTests — AddKnowledgeRetrieval's
/// ValidateOnStart must fail the host fast when GEMINI_API_KEY/CHROMA_BASE_URL are missing or
/// blank, and must not perform any network call merely to start when both are valid.
/// </summary>
public class GeminiChromaOptionsValidationTests
{
    [Fact]
    public void Host_WithoutGeminiApiKey_FailsToStart()
    {
        using var factory = McpServerTestHost.CreateWithoutGeminiApiKey();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Host_WithBlankGeminiApiKey_FailsToStart()
    {
        using var factory = McpServerTestHost.CreateWithBlankGeminiApiKey();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Host_WithoutChromaBaseUrl_FailsToStart()
    {
        using var factory = McpServerTestHost.CreateWithoutChromaBaseUrl();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Host_WithBlankChromaBaseUrl_FailsToStart()
    {
        using var factory = McpServerTestHost.CreateWithBlankChromaBaseUrl();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    /// <summary>Regression coverage for the ambient-override bug found during P0-03 live-credential
    /// verification (see McpServerTestHost's class doc for the full mechanism): a "without X" test
    /// must keep failing to start even when a real-looking value for that same key is present in a
    /// lower-priority configuration source (standing in for a real ambient environment variable —
    /// this never touches the actual process environment). Covers both protected keys via one
    /// shared design rather than duplicating a Fact per key.</summary>
    [Theory]
    [InlineData(GeminiEmbeddingOptions.ApiKeyConfigKey)]
    [InlineData(ChromaOptions.BaseUrlConfigKey)]
    public void Host_MissingKey_FailsToStart_EvenWithASimulatedAmbientValuePresent(string configKey)
    {
        using var factory = McpServerTestHost.CreateWithSimulatedAmbientValueMaskedByNullTombstone(configKey, "simulated-ambient-real-looking-value");

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Host_WithValidGeminiApiKeyAndChromaBaseUrl_StartsSuccessfully()
    {
        // No real Gemini/Chroma service needs to be reachable for the host to start —
        // ValidateOnStart only checks non-empty/URI syntax. The host booting successfully here,
        // with nothing listening on port 8000 and a fake API key, is itself evidence that no
        // network call happens during startup (the Google.GenAI Client and Chroma HttpClient are
        // only ever used lazily when IEmbeddingClient/IVectorStore are first resolved, which
        // nothing does yet).
        await using var factory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }
}
