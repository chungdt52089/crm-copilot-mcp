using System.Text.Json;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;

namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Explicit, field-by-field parsing of a Chroma metadata object into the fully-typed
/// KnowledgeSourceMetadata. Deliberately not JsonSerializer.Deserialize&lt;Dictionary&lt;string,
/// object&gt;&gt; — System.Text.Json boxes object-typed dictionary values as JsonElement, so a
/// naive read would make later equality comparisons (e.g. the ingestion fingerprint skip-check)
/// silently wrong. Every missing/wrong-kind field throws KnowledgeVectorStoreException naming
/// the sourceId and field — never an unhandled InvalidOperationException from JsonElement
/// itself reaching the caller.
/// </summary>
internal static class ChromaMetadataParser
{
    public static KnowledgeSourceMetadata Parse(string sourceId, IReadOnlyDictionary<string, JsonElement> raw)
    {
        string GetString(string key)
        {
            if (raw.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString()!;
            }

            throw new KnowledgeVectorStoreException(
                $"Chroma metadata for '{sourceId}' is missing or has a non-string '{key}'.", retryable: false);
        }

        string? GetOptionalString(string key) =>
            raw.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        int GetInt(string key)
        {
            if (raw.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
            {
                return value;
            }

            throw new KnowledgeVectorStoreException(
                $"Chroma metadata for '{sourceId}' is missing or has a non-integer '{key}'.", retryable: false);
        }

        bool GetBool(string key)
        {
            if (raw.TryGetValue(key, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return element.GetBoolean();
            }

            throw new KnowledgeVectorStoreException(
                $"Chroma metadata for '{sourceId}' is missing or has a non-boolean '{key}'.", retryable: false);
        }

        var documentTypeWire = GetString("documentType");

        return new KnowledgeSourceMetadata(
            SourceId: GetString("sourceId"),
            DocumentType: KnowledgeDocumentTypeWireFormat.FromWire(sourceId, documentTypeWire),
            ProductCode: GetOptionalString("productCode"),
            TemplateId: GetOptionalString("templateId"),
            Language: GetString("language"),
            Version: GetString("version"),
            EmbeddingModel: GetString("embeddingModel"),
            EmbeddingDimension: GetInt("embeddingDimension"),
            DistanceMetric: GetString("distanceMetric"),
            Normalized: GetBool("normalized"),
            Fingerprint: GetString("fingerprint"));
    }

    /// <summary>Write side — building outbound JSON is unambiguous (values are concrete runtime
    /// types System.Text.Json serializes correctly), unlike the boxed-JsonElement read side
    /// above. Null ProductCode/TemplateId are omitted entirely rather than sent as JSON null,
    /// since Chroma metadata values are expected to be non-null scalars.</summary>
    public static Dictionary<string, object> ToWireMetadata(KnowledgeSourceMetadata metadata)
    {
        var wire = new Dictionary<string, object>
        {
            ["sourceId"] = metadata.SourceId,
            ["documentType"] = KnowledgeDocumentTypeWireFormat.ToWire(metadata.DocumentType),
            ["language"] = metadata.Language,
            ["version"] = metadata.Version,
            ["embeddingModel"] = metadata.EmbeddingModel,
            ["embeddingDimension"] = metadata.EmbeddingDimension,
            ["distanceMetric"] = metadata.DistanceMetric,
            ["normalized"] = metadata.Normalized,
            ["fingerprint"] = metadata.Fingerprint,
        };

        if (metadata.ProductCode is not null)
        {
            wire["productCode"] = metadata.ProductCode;
        }

        if (metadata.TemplateId is not null)
        {
            wire["templateId"] = metadata.TemplateId;
        }

        return wire;
    }
}
