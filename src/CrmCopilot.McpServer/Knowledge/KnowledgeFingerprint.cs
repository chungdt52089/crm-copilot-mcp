using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Covers everything that determines whether a stored vector is still valid — not just the
/// rendered source text. A raw hash of renderedText alone would let a future embedding
/// model/dimension/distance-metric change leave renderedText byte-identical, so the ingestion
/// skip-check would wrongly skip re-embedding stale-model vectors (silently violating PD-009's
/// "changing embedding model mandates re-embed/re-index"). Bumping RendererSchemaVersion forces
/// a full re-embed even in the (unlikely) case a renderer bugfix produces identical output for
/// every current input.
/// </summary>
internal static class KnowledgeFingerprint
{
    public const string RendererSchemaVersion = "v1";

    private const char FieldSeparator = '\u001F'; // non-printable field delimiter -- prevents
                                                   // naive-concatenation collisions between fields

    public static string Compute(string renderedText, string embeddingModel, int embeddingDimension, string distanceMetric)
    {
        var input = string.Join(
            FieldSeparator,
            RendererSchemaVersion,
            embeddingModel,
            embeddingDimension.ToString(CultureInfo.InvariantCulture),
            distanceMetric,
            renderedText);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
