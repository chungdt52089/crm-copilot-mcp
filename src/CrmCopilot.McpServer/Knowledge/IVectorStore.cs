using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.McpServer.Knowledge;

internal interface IVectorStore
{
    /// <summary>Pure liveness check — GET /api/v2/heartbeat only. Deliberately does not resolve
    /// or create the collection, so "is Chroma reachable" stays a distinct signal from "is our
    /// collection correctly configured".</summary>
    Task<bool> HeartbeatAsync(CancellationToken cancellationToken);

    /// <summary>Null if no record exists yet for this id.</summary>
    Task<KnowledgeSourceMetadata?> TryGetMetadataAsync(string id, CancellationToken cancellationToken);

    Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<VectorMatch>> QueryAsync(
        double[] queryEmbedding, int topK, IReadOnlyList<string>? documentTypeFilter, CancellationToken cancellationToken);

    /// <summary>Total record count in the collection — used only for live acceptance evidence
    /// (README), not by any production code path.</summary>
    Task<int> CountAsync(CancellationToken cancellationToken);
}
