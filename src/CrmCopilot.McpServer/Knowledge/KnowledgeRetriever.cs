using CrmCopilot.Contracts.Knowledge;
using Microsoft.Extensions.Options;

namespace CrmCopilot.McpServer.Knowledge;

internal sealed class KnowledgeRetriever(
    IEmbeddingClient embeddingClient, IVectorStore vectorStore, IOptions<KnowledgeRetrievalOptions> retrievalOptions)
    : IKnowledgeRetriever
{
    private const int MaxQueryTextLength = 2000; // generous, defensive bound — not a precisely
                                                   // researched Gemini input limit
    private const int MinTopK = 1;
    private const int MaxTopK = 20; // mirrors MockCrmGateway.GetInteractionsAsync's limit bound

    public async Task<KnowledgeSearchResult> SearchAsync(KnowledgeSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.QueryText);

        if (query.QueryText.Length > MaxQueryTextLength)
        {
            throw new ArgumentException($"QueryText exceeds {MaxQueryTextLength} characters.", nameof(query));
        }

        if (query.TopK is < MinTopK or > MaxTopK)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.TopK, $"TopK must be between {MinTopK} and {MaxTopK}.");
        }

        var embedding = await embeddingClient.EmbedQueryAsync(query.QueryText, cancellationToken).ConfigureAwait(false);
        var documentTypeFilter = MapDocumentTypeFilter(query.DocumentTypes);
        var matches = await vectorStore.QueryAsync(embedding, query.TopK, documentTypeFilter, cancellationToken).ConfigureAwait(false);

        var accepted = matches.Where(m => m.Distance <= retrievalOptions.Value.MaxDistance).ToList();

        return accepted.Count == 0
            ? KnowledgeSearchResult.NoRelevantEvidence
            : KnowledgeSearchResult.Found(accepted.Select(ToKnowledgeMatch).ToList());
    }

    private static IReadOnlyList<string>? MapDocumentTypeFilter(IReadOnlyList<KnowledgeDocumentType>? documentTypes) =>
        documentTypes is { Count: > 0 }
            ? documentTypes.Select(KnowledgeDocumentTypeWireFormat.ToWire).ToList()
            : null;

    private static KnowledgeMatch ToKnowledgeMatch(VectorMatch match) =>
        new(match.Id, match.Metadata.DocumentType, match.Document, match.Metadata, match.Distance);
}
