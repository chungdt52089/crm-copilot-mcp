using CrmCopilot.McpServer.Knowledge;

namespace CrmCopilot.Tests.Knowledge.TestSupport;

/// <summary>
/// Deterministic offline stand-in for IEmbeddingClient. Vector content is not meant to carry
/// real semantic meaning — KnowledgeRetrieverTests controls FakeVectorStore's returned distances
/// directly, so this only needs to (a) be deterministic and 768-dimensional/normalized, and
/// (b) support call counting and on-demand failure injection.
/// </summary>
internal sealed class FakeEmbeddingClient : IEmbeddingClient
{
    public int DocumentCallCount { get; private set; }
    public int QueryCallCount { get; private set; }
    public Exception? ThrowOnNextCall { get; set; }

    public Task<double[]> EmbedDocumentAsync(string text, CancellationToken cancellationToken)
    {
        DocumentCallCount++;
        return Complete(text, cancellationToken);
    }

    public Task<double[]> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        QueryCallCount++;
        return Complete(text, cancellationToken);
    }

    private Task<double[]> Complete(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ThrowOnNextCall is { } exception)
        {
            ThrowOnNextCall = null;
            throw exception;
        }

        return Task.FromResult(DeterministicVector(text));
    }

    public static double[] DeterministicVector(string text)
    {
        var random = new Random(text.GetHashCode(StringComparison.Ordinal));
        var raw = new double[GeminiEmbeddingOptions.Dimension];
        for (var i = 0; i < raw.Length; i++)
        {
            raw[i] = random.NextDouble() - 0.5;
        }

        return EmbeddingMath.L2Normalize(raw);
    }
}
