namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Kept as a plain class (not a record) deliberately — a record's compiler-generated ToString()
/// would print every property including ApiKey; a class's default ToString() does not.
/// </summary>
public sealed class GeminiEmbeddingOptions
{
    /// <summary>Flat config key, matching .env.example's GEMINI_API_KEY exactly (docs/06-style
    /// convention already used by MockCrmGatewayOptions.ConfigKey).</summary>
    public const string ApiKeyConfigKey = "GEMINI_API_KEY";

    public const string ModelId = "gemini-embedding-001";
    public const int Dimension = 768;

    public string ApiKey { get; set; } = string.Empty;
}
