namespace CrmCopilot.Web.Chat;

/// <summary>
/// Kept as a plain class (not a record) deliberately — a record's compiler-generated ToString()
/// would print every property including ApiKey; a class's default ToString() does not. Same
/// reasoning as CrmCopilot.McpServer.Knowledge.GeminiEmbeddingOptions.
/// </summary>
public sealed class GeminiChatOptions
{
    /// <summary>Flat config key, same env var name as McpServer's GeminiEmbeddingOptions — read
    /// independently by this (separate) process.</summary>
    public const string ApiKeyConfigKey = "GEMINI_API_KEY";

    public const string ModelId = "gemini-3.5-flash-lite";

    public string ApiKey { get; set; } = string.Empty;
}
