using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.McpServer.Knowledge.Ingestion;

/// <summary>
/// Idempotent per-document ingestion: a document is only re-embedded when its fingerprint
/// (renderer schema version + embedding model + dimension + distance metric + rendered text —
/// see KnowledgeFingerprint) differs from what is already stored. A second run over unchanged
/// source data therefore makes zero Gemini embedding calls, not just "no duplicate Chroma rows"
/// (which upsert-by-stable-id would already guarantee on its own).
/// </summary>
internal sealed class KnowledgeIngestionService(IEmbeddingClient embeddingClient, IVectorStore vectorStore)
{
    public async Task<KnowledgeIngestionSummary> IngestAsync(IReadOnlyList<KnowledgeSourceDocument> documents, CancellationToken cancellationToken)
    {
        var embedded = 0;
        var skipped = 0;

        foreach (var document in documents)
        {
            var fingerprint = KnowledgeFingerprint.Compute(
                document.RenderedText, GeminiEmbeddingOptions.ModelId, GeminiEmbeddingOptions.Dimension, ChromaOptions.DistanceMetric);

            var existing = await vectorStore.TryGetMetadataAsync(document.SourceId, cancellationToken).ConfigureAwait(false);

            if (existing is not null && existing.Fingerprint == fingerprint)
            {
                skipped++;
                continue;
            }

            var embedding = await embeddingClient.EmbedDocumentAsync(document.RenderedText, cancellationToken).ConfigureAwait(false);

            var metadata = new KnowledgeSourceMetadata(
                SourceId: document.SourceId,
                DocumentType: document.DocumentType,
                ProductCode: document.ProductCode,
                TemplateId: document.TemplateId,
                Language: document.Language,
                Version: document.Version,
                EmbeddingModel: GeminiEmbeddingOptions.ModelId,
                EmbeddingDimension: GeminiEmbeddingOptions.Dimension,
                DistanceMetric: ChromaOptions.DistanceMetric,
                Normalized: true,
                Fingerprint: fingerprint);

            await vectorStore.UpsertAsync(
                new VectorRecord(document.SourceId, embedding, document.RenderedText, metadata), cancellationToken).ConfigureAwait(false);
            embedded++;
        }

        return new KnowledgeIngestionSummary(documents.Count, embedded, skipped);
    }
}
