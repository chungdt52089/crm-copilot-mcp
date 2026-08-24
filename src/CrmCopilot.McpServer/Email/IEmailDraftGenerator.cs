namespace CrmCopilot.McpServer.Email;

/// <summary>
/// Wraps a single Gemini generateContent call for generate_email's structured-output draft
/// generation. Model id/temperature are fixed (<see cref="EmailGenerationOptions"/>), same
/// convention as CrmCopilot.McpServer.Knowledge.IEmbeddingClient/CrmCopilot.Web.Chat.IGeminiChatClient
/// baking in their own model id constants.
/// </summary>
internal interface IEmailDraftGenerator
{
    /// <summary>
    /// Throws <see cref="EmailGenerationException"/> only for a true Gemini call failure (transport/
    /// ClientError/ServerError) or an empty/missing response text. Returns <c>null</c> — not an
    /// exception — when Gemini answered but the text failed to deserialize into
    /// <see cref="RawEmailDraftModel"/>: that is a retryable *business* validation failure the
    /// caller's own retry loop owns, kept deliberately separate from an infra failure.
    /// </summary>
    Task<RawEmailDraftModel?> GenerateAsync(EmailDraftPromptContext context, CancellationToken cancellationToken);
}
