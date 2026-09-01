using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Knowledge;

namespace CrmCopilot.Tests.Knowledge.TestSupport;

/// <summary>
/// In-memory offline stand-in for IVectorStore. QueryAsync defaults to brute-force nearest by
/// squared L2 distance over whatever has been Upsert-ed, but tests that need precise, hand-picked
/// distances (independent of the fake embedding geometry) can set QueryResultOverride instead.
/// </summary>
internal sealed class FakeVectorStore : IVectorStore
{
    private readonly Dictionary<string, (double[] Embedding, string Document, KnowledgeSourceMetadata Metadata)> _records = new();

    public bool HeartbeatResult { get; set; } = true;
    public int UpsertCallCount { get; private set; }
    public Exception? ThrowOnQuery { get; set; }
    public IReadOnlyList<VectorMatch>? QueryResultOverride { get; set; }
    public int RecordCount => _records.Count;
    public int? LastRequestedTopK { get; private set; }
    public IReadOnlyList<string>? LastDocumentTypeFilter { get; private set; }

    public Task<bool> HeartbeatAsync(CancellationToken cancellationToken) => Task.FromResult(HeartbeatResult);

    public Task<KnowledgeSourceMetadata?> TryGetMetadataAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_records.TryGetValue(id, out var record) ? record.Metadata : null);

    public Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken)
    {
        UpsertCallCount++;
        _records[record.Id] = (record.Embedding, record.Document, record.Metadata);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorMatch>> QueryAsync(
        double[] queryEmbedding, int topK, IReadOnlyList<string>? documentTypeFilter, CancellationToken cancellationToken)
    {
        LastRequestedTopK = topK;
        LastDocumentTypeFilter = documentTypeFilter;

        if (ThrowOnQuery is { } exception)
        {
            throw exception;
        }

        if (QueryResultOverride is not null)
        {
            return Task.FromResult(QueryResultOverride);
        }

        var ranked = _records
            .Where(kv => documentTypeFilter is not { Count: > 0 } ||
                         documentTypeFilter.Contains(KnowledgeDocumentTypeWireFormat.ToWire(kv.Value.Metadata.DocumentType)))
            .Select(kv => new VectorMatch(kv.Key, kv.Value.Document, kv.Value.Metadata, SquaredL2Distance(queryEmbedding, kv.Value.Embedding)))
            .OrderBy(match => match.Distance)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<VectorMatch>>(ranked);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(_records.Count);

    private static double SquaredL2Distance(double[] a, double[] b)
    {
        var sum = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }

        return sum;
    }
}
