using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using Microsoft.Extensions.Options;

namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Thin hand-rolled HTTP adapter over Chroma's v2 REST API (docs/13_REFERENCE_SOURCES.md — no
/// official/reviewed .NET Chroma client exists). Wire request/response DTOs below intentionally
/// use Chroma's own snake_case field names as C# record property names, scoped to this one file,
/// to avoid [JsonPropertyName] noise everywhere.
/// </summary>
internal sealed class ChromaHttpClient(HttpClient httpClient, IOptions<ChromaOptions> options) : IVectorStore
{
    private static readonly JsonSerializerOptions WireJsonOptions = new();

    // Cache-only-on-success: a failed/canceled resolution must never poison later calls (unlike
    // a naive Lazy<Task<string>>, which permanently caches a faulted task).
    private volatile string? _cachedCollectionId;
    private readonly SemaphoreSlim _resolutionLock = new(1, 1);

    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken)
    {
        // Deliberately does not call ResolveCollectionIdAsync — "is Chroma reachable" must stay
        // independent from "is our collection correctly configured".
        try
        {
            using var response = await httpClient.GetAsync("/api/v2/heartbeat", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<KnowledgeSourceMetadata?> TryGetMetadataAsync(string id, CancellationToken cancellationToken)
    {
        var collectionId = await ResolveCollectionIdAsync(cancellationToken).ConfigureAwait(false);
        var request = new GetRequest([id], ["metadatas"]);

        using var response = await PostAsync(CollectionPath(collectionId) + "/get", request, cancellationToken).ConfigureAwait(false);
        var body = await ReadAsync<GetResponse>(response, cancellationToken).ConfigureAwait(false);

        if (body.ids is not { Count: > 0 } || body.metadatas is not { Count: > 0 })
        {
            return null;
        }

        return ChromaMetadataParser.Parse(id, body.metadatas[0]);
    }

    public async Task UpsertAsync(VectorRecord record, CancellationToken cancellationToken)
    {
        var collectionId = await ResolveCollectionIdAsync(cancellationToken).ConfigureAwait(false);
        var request = new UpsertRequest(
            [record.Id],
            [record.Embedding],
            [record.Document],
            [ChromaMetadataParser.ToWireMetadata(record.Metadata)]);

        using var response = await PostAsync(CollectionPath(collectionId) + "/upsert", request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorMatch>> QueryAsync(
        double[] queryEmbedding, int topK, IReadOnlyList<string>? documentTypeFilter, CancellationToken cancellationToken)
    {
        var collectionId = await ResolveCollectionIdAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<string, object>? where = documentTypeFilter is { Count: > 0 }
            ? new Dictionary<string, object> { ["documentType"] = new Dictionary<string, object> { ["$in"] = documentTypeFilter } }
            : null;

        var request = new QueryRequest([queryEmbedding], topK, where, ["documents", "metadatas", "distances"]);
        using var response = await PostAsync(CollectionPath(collectionId) + "/query", request, cancellationToken).ConfigureAwait(false);
        var body = await ReadAsync<QueryResponse>(response, cancellationToken).ConfigureAwait(false);

        if (body.ids is not { Count: > 0 } || body.ids[0].Count == 0)
        {
            return [];
        }

        var ids = body.ids[0];
        var distances = body.distances?.ElementAtOrDefault(0)
            ?? throw new KnowledgeVectorStoreException("Chroma query response is missing distances.", retryable: false);
        var metadatas = body.metadatas?.ElementAtOrDefault(0)
            ?? throw new KnowledgeVectorStoreException("Chroma query response is missing metadatas.", retryable: false);
        var documents = body.documents?.ElementAtOrDefault(0)
            ?? throw new KnowledgeVectorStoreException("Chroma query response is missing documents.", retryable: false);

        var matches = new List<VectorMatch>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            matches.Add(new VectorMatch(ids[i], documents[i], ChromaMetadataParser.Parse(ids[i], metadatas[i]), distances[i]));
        }

        return matches;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        var collectionId = await ResolveCollectionIdAsync(cancellationToken).ConfigureAwait(false);
        using var response = await SendAsync(HttpMethod.Get, CollectionPath(collectionId) + "/count", null, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<int>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ResolveCollectionIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedCollectionId is { } cached)
        {
            return cached;
        }

        await _resolutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedCollectionId is { } resolved)
            {
                return resolved; // another caller won the race while we waited
            }

            var id = await CreateOrGetCollectionAsync(cancellationToken).ConfigureAwait(false); // throws — never cached on failure
            _cachedCollectionId = id; // only assigned after success
            return id;
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    private async Task<string> CreateOrGetCollectionAsync(CancellationToken cancellationToken)
    {
        var request = new CreateCollectionRequest(
            options.Value.CollectionName,
            get_or_create: true,
            new CollectionConfiguration(new HnswConfiguration(ChromaOptions.DistanceMetric)),
            new Dictionary<string, object>
            {
                ["embeddingModel"] = GeminiEmbeddingOptions.ModelId,
                ["embeddingDimension"] = GeminiEmbeddingOptions.Dimension,
                ["distanceMetric"] = ChromaOptions.DistanceMetric,
            });

        using var response = await PostAsync(
            $"/api/v2/tenants/{ChromaOptions.Tenant}/databases/{ChromaOptions.Database}/collections", request, cancellationToken)
            .ConfigureAwait(false);
        var body = await ReadAsync<CollectionResponse>(response, cancellationToken).ConfigureAwait(false);

        CheckCompatibility(body.metadata);
        return body.id;
    }

    /// <summary>Only compares fields Chroma actually returned — our own get_or_create call
    /// always writes embeddingModel/embeddingDimension/distanceMetric, so a genuinely
    /// incompatible pre-existing collection (created by an earlier version of this checkpoint's
    /// code before a model/dimension/metric bump) is exactly the case this catches
    /// (PD-009: a model/dimension change requires a new or re-indexed collection).</summary>
    private static void CheckCompatibility(Dictionary<string, JsonElement>? existingMetadata)
    {
        if (existingMetadata is null)
        {
            return;
        }

        var existingModel = TryGetString(existingMetadata, "embeddingModel");
        var existingDimension = TryGetInt(existingMetadata, "embeddingDimension");
        var existingMetric = TryGetString(existingMetadata, "distanceMetric");

        var mismatched =
            (existingModel is not null && existingModel != GeminiEmbeddingOptions.ModelId) ||
            (existingDimension is not null && existingDimension != GeminiEmbeddingOptions.Dimension) ||
            (existingMetric is not null && existingMetric != ChromaOptions.DistanceMetric);

        if (mismatched)
        {
            throw new KnowledgeVectorStoreException(
                "Existing Chroma collection's embeddingModel/embeddingDimension/distanceMetric do not match " +
                "the configured values — delete and recreate the collection before ingesting.",
                retryable: false);
        }
    }

    private static string? TryGetString(Dictionary<string, JsonElement> metadata, string key) =>
        metadata.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static int? TryGetInt(Dictionary<string, JsonElement> metadata, string key) =>
        metadata.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : null;

    private static string CollectionPath(string collectionId) =>
        $"/api/v2/tenants/{ChromaOptions.Tenant}/databases/{ChromaOptions.Database}/collections/{collectionId}";

    private async Task<HttpResponseMessage> PostAsync<TRequest>(string requestUri, TRequest body, CancellationToken cancellationToken)
    {
        var content = JsonContent.Create(body, options: WireJsonOptions);
        return await SendAsync(HttpMethod.Post, requestUri, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri) { Content = content };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new KnowledgeVectorStoreException("Failed to reach Chroma.", retryable: true, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's own timeout fired (not the caller's token) — transport-level, never
            // surfaced as a caller-cancellation.
            throw new KnowledgeVectorStoreException("Chroma request timed out.", retryable: true, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            var statusCode = (int)response.StatusCode;
            var retryable = statusCode is 408 or 429 || statusCode >= 500;
            throw new KnowledgeVectorStoreException($"Chroma returned unexpected status {statusCode}.", retryable);
        }

        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<T>(WireJsonOptions, cancellationToken).ConfigureAwait(false);
            return result is not null
                ? result
                : throw new KnowledgeVectorStoreException("Chroma returned an empty response body.", retryable: false);
        }
        catch (JsonException ex)
        {
            throw new KnowledgeVectorStoreException("Chroma returned a response that could not be parsed.", retryable: false, ex);
        }
    }

    // --- wire DTOs: Chroma's own field names, snake_case, private to this adapter ---

    private sealed record CreateCollectionRequest(string name, bool get_or_create, CollectionConfiguration configuration, Dictionary<string, object> metadata);
    private sealed record CollectionConfiguration(HnswConfiguration hnsw);
    private sealed record HnswConfiguration(string space);
    private sealed record CollectionResponse(string id, string name, Dictionary<string, JsonElement>? metadata);

    private sealed record GetRequest(List<string> ids, List<string> include);
    private sealed record GetResponse(List<string>? ids, List<Dictionary<string, JsonElement>>? metadatas);

    private sealed record UpsertRequest(List<string> ids, List<double[]> embeddings, List<string> documents, List<Dictionary<string, object>> metadatas);

    private sealed record QueryRequest(List<double[]> query_embeddings, int n_results, Dictionary<string, object>? where, List<string> include);
    private sealed record QueryResponse(
        List<List<string>>? ids,
        List<List<double>>? distances,
        List<List<Dictionary<string, JsonElement>>>? metadatas,
        List<List<string>>? documents);
}
