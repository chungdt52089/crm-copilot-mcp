namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// Wraps a single Gemini generateContent call for the generate_call_script structured output.
/// Model id/temperature are fixed (<see cref="CallScriptGenerationOptions"/>), same convention as
/// IEmailDraftGenerator/IEmbeddingClient.
/// </summary>
internal interface ICallScriptGenerator
{
    /// <summary>
    /// Throws <see cref="CallScriptGenerationException"/> only for a true Gemini call failure
    /// (transport/ClientError/ServerError) or an empty/missing response text. Returns
    /// <c>null</c> — not an exception — when Gemini answered but the text failed to deserialize:
    /// that is a retryable business validation failure the caller retry loop owns, kept
    /// deliberately separate from an infra failure.
    /// </summary>
    Task<RawCallScriptModel?> GenerateAsync(CallScriptPromptContext context, CancellationToken cancellationToken);
}
