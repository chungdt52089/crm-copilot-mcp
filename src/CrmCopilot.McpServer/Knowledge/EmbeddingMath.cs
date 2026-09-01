namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Pure vector math, independently unit-testable without touching the Gemini SDK or network.
/// Manual L2 normalization is required for gemini-embedding-001 at non-3072 output dimensions
/// (docs/13_REFERENCE_SOURCES.md, verified against ai.google.dev/gemini-api/docs/embeddings).
/// </summary>
internal static class EmbeddingMath
{
    public static double[] L2Normalize(IReadOnlyList<double> values)
    {
        var sumOfSquares = 0d;
        foreach (var v in values)
        {
            sumOfSquares += v * v;
        }

        var norm = Math.Sqrt(sumOfSquares);
        if (norm == 0d)
        {
            throw new ArgumentException("Cannot L2-normalize a zero vector.", nameof(values));
        }

        var result = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            result[i] = values[i] / norm;
        }

        return result;
    }

    public static void ValidateDimension(IReadOnlyList<double> values, int expectedDimension)
    {
        if (values.Count != expectedDimension)
        {
            throw new ArgumentException(
                $"Expected a {expectedDimension}-dimension vector but got {values.Count}.", nameof(values));
        }
    }
}
