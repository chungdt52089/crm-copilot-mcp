namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Internal seam over the Gemini embedding call — nothing outside CrmCopilot.McpServer needs to
/// pick a task type directly (ingestion always uses RETRIEVAL_DOCUMENT, retrieval always uses
/// RETRIEVAL_QUERY), so this is not part of the public Contracts surface.
/// </summary>
internal interface IEmbeddingClient
{
    /// <summary>Returns an L2-normalized 768-dimension vector. Throws
    /// CrmCopilot.Contracts.Knowledge.Exceptions.KnowledgeEmbeddingException on any failure.</summary>
    Task<double[]> EmbedDocumentAsync(string text, CancellationToken cancellationToken);

    /// <summary>Returns an L2-normalized 768-dimension vector. Throws
    /// CrmCopilot.Contracts.Knowledge.Exceptions.KnowledgeEmbeddingException on any failure.</summary>
    Task<double[]> EmbedQueryAsync(string text, CancellationToken cancellationToken);
}
