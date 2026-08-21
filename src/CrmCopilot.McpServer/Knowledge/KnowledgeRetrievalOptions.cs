namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// TopK deliberately lives only on KnowledgeSearchQuery (caller-supplied), not here — this type
/// holds only the server-side threshold, so there is exactly one source of truth for TopK.
/// </summary>
public sealed class KnowledgeRetrievalOptions
{
    /// <summary>
    /// Maximum accepted Chroma "l2" (squared L2, range 0-4 on our L2-normalized vectors)
    /// distance. Reasoned starting point, not yet empirically calibrated against real
    /// gemini-embedding-001 output — the mandatory live acceptance run (README) is where this
    /// gets sanity-checked against real distances before the checkpoint is reported PASS.
    /// Offline tests use contrived fake distances and do not depend on this exact number.
    /// </summary>
    public double MaxDistance { get; set; } = 1.2;
}
