using Google.GenAI.Types;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Wraps a single Gemini generateContent call for the P0-05 tool-calling loop. Model id is fixed
/// (<see cref="GeminiChatOptions.ModelId"/>) and not a parameter — same convention as
/// CrmCopilot.McpServer.Knowledge.IEmbeddingClient baking in GeminiEmbeddingOptions.ModelId.
/// Returns the SDK's own <see cref="GenerateContentResponse"/> directly rather than a parallel
/// DTO — the response shape (candidates/function calls/text) is exactly what the orchestrator
/// needs, so remapping it would be pure overhead (plan D-decision).
/// </summary>
internal interface IGeminiChatClient
{
    /// <summary>Throws <see cref="ChatModelException"/> for any Gemini call failure.</summary>
    Task<GenerateContentResponse> GenerateAsync(
        IReadOnlyList<Content> contents,
        GenerateContentConfig config,
        CancellationToken cancellationToken);
}
