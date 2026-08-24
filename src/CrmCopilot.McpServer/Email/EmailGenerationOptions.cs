namespace CrmCopilot.McpServer.Email;

/// <summary>
/// Fixed constants for the generate_email Gemini generation call. Plain constants, not an
/// IOptions&lt;T&gt; — unlike GeminiEmbeddingOptions/GeminiChatOptions there is no secret here to
/// bind from configuration.
/// </summary>
internal static class EmailGenerationOptions
{
    public const string ModelId = "gemini-3.5-flash-lite";

    /// <summary>Low temperature for grounded, low-variance generation (docs/08_RAG_EMAIL_AND_PII_SPEC.md
    /// §9: "Low temperature cho generation").</summary>
    public const double Temperature = 0.2;

    /// <summary>1 initial attempt + 1 retry (docs/08_RAG_EMAIL_AND_PII_SPEC.md §9: "Schema retry
    /// tối đa 1 lần"). Never a third attempt.</summary>
    public const int MaxAttempts = 2;
}
