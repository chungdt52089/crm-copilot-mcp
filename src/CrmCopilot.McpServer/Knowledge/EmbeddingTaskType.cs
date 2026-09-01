namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Our own seam over Gemini's string-typed EmbedContentConfig.TaskType (the Google.GenAI SDK
/// does not expose an enum for it) — keeps the RETRIEVAL_DOCUMENT/RETRIEVAL_QUERY string
/// literals in exactly one place (GeminiEmbeddingClient.MapTaskType).
/// </summary>
internal enum EmbeddingTaskType
{
    Document,
    Query,
}
