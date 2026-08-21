using System.Net;
using System.Text;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.McpServer.Knowledge;
using Microsoft.Extensions.Options;

namespace CrmCopilot.Tests.Knowledge;

/// <summary>
/// Stub-HttpMessageHandler coverage for ChromaHttpClient, mirroring
/// CrmCopilot.Tests.Crm.MockCrmGatewayTests' StubHandler/ThrowingHandler pattern.
/// </summary>
public class ChromaHttpClientTests
{
    private const string CollectionId = "11111111-1111-1111-1111-111111111111";

    private const string MatchingCollectionResponse = """
        {"id": "11111111-1111-1111-1111-111111111111", "name": "crm-copilot-knowledge", "metadata": {"embeddingModel": "gemini-embedding-001", "embeddingDimension": 768, "distanceMetric": "l2"}}
        """;

    private const string MismatchedCollectionResponse = """
        {"id": "11111111-1111-1111-1111-111111111111", "name": "crm-copilot-knowledge", "metadata": {"embeddingModel": "old-model", "embeddingDimension": 512, "distanceMetric": "cosine"}}
        """;

    private static ChromaHttpClient CreateClient(RecordingHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            Options.Create(new ChromaOptions { BaseUrl = "http://localhost", CollectionName = "crm-copilot-knowledge" }));

    // --- heartbeat: independent of collection resolution ---

    [Fact]
    public async Task HeartbeatAsync_ServerUp_ReturnsTrue_WithoutResolvingCollection()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/api/v2/heartbeat"
            ? JsonResponse(HttpStatusCode.OK, """{"nanosecond heartbeat": 123}""")
            : JsonResponse(HttpStatusCode.InternalServerError, "should not be called"));

        var result = await CreateClient(handler).HeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/heartbeat", handler.Requests[0].Path);
    }

    [Fact]
    public async Task HeartbeatAsync_TransportFailure_ReturnsFalse()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await CreateClient(handler).HeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    // --- collection resolution: caching, failure isolation, compatibility check ---

    [Fact]
    public async Task ResolveCollection_FailsThenSucceeds_DoesNotPoisonSubsequentCalls()
    {
        var attempt = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal))
            {
                attempt++;
                return attempt == 1
                    ? JsonResponse(HttpStatusCode.InternalServerError, "transient failure")
                    : JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse);
            }

            return JsonResponse(HttpStatusCode.OK, EmptyGetResponse());
        });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<KnowledgeVectorStoreException>(
            () => client.TryGetMetadataAsync("kb:product:PRD-01", TestContext.Current.CancellationToken));

        // A failed resolution must not be cached — the next call gets a fresh attempt, which
        // succeeds this time (the second stub response), not a poisoned/replayed exception.
        var result = await client.TryGetMetadataAsync("kb:product:PRD-01", TestContext.Current.CancellationToken);

        Assert.Null(result); // not-found, but not an exception
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task ResolveCollection_ExistingCollectionWithMismatchedMetadata_ThrowsNonRetryable()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MismatchedCollectionResponse)
            : JsonResponse(HttpStatusCode.OK, EmptyGetResponse()));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<KnowledgeVectorStoreException>(
            () => client.TryGetMetadataAsync("kb:product:PRD-01", TestContext.Current.CancellationToken));

        Assert.False(exception.Retryable);
    }

    // --- upsert / get / query wire shapes ---

    [Fact]
    public async Task UpsertAsync_SendsExpectedRequestShape()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse)
            : JsonResponse(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        var metadata = new KnowledgeSourceMetadata(
            "kb:product:PRD-01", KnowledgeDocumentType.Product, "PRD-01", null,
            "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fingerprint-1");

        await client.UpsertAsync(new VectorRecord("kb:product:PRD-01", [0.1, 0.2, 0.3], "rendered text", metadata), TestContext.Current.CancellationToken);

        var createRequest = handler.Requests[0];
        Assert.Contains("\"get_or_create\":true", createRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"hnsw\":{\"space\":\"l2\"}", createRequest.Body, StringComparison.Ordinal);

        var upsertRequest = handler.Requests[1];
        Assert.EndsWith($"/collections/{CollectionId}/upsert", upsertRequest.Path, StringComparison.Ordinal);
        Assert.Contains("\"ids\":[\"kb:product:PRD-01\"]", upsertRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"documents\":[\"rendered text\"]", upsertRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"documentType\":\"product\"", upsertRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"fingerprint\":\"fingerprint-1\"", upsertRequest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"templateId\"", upsertRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_WithDocumentTypeFilter_SendsInOperatorShape()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse)
            : JsonResponse(HttpStatusCode.OK, EmptyQueryResponse()));
        var client = CreateClient(handler);

        await client.QueryAsync([0.1, 0.2], topK: 3, documentTypeFilter: ["product", "email_template"], TestContext.Current.CancellationToken);

        var queryRequest = handler.Requests[1];
        Assert.Contains("""{"documentType":{"$in":["product","email_template"]}}""", queryRequest.Body, StringComparison.Ordinal);
        Assert.Contains("\"n_results\":3", queryRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_NoFilter_OmitsWhereClause()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse)
            : JsonResponse(HttpStatusCode.OK, EmptyQueryResponse()));
        var client = CreateClient(handler);

        await client.QueryAsync([0.1, 0.2], topK: 3, documentTypeFilter: null, TestContext.Current.CancellationToken);

        var queryRequest = handler.Requests[1];
        Assert.Contains("\"where\":null", queryRequest.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_ParsesIdsDistancesMetadatasDocuments()
    {
        const string queryResponse = """
            {
              "ids": [["kb:product:PRD-01"]],
              "distances": [[0.42]],
              "documents": [["rendered text"]],
              "metadatas": [[{
                "sourceId": "kb:product:PRD-01", "documentType": "product", "productCode": "PRD-01",
                "language": "vi", "version": "1.0", "embeddingModel": "gemini-embedding-001",
                "embeddingDimension": 768, "distanceMetric": "l2", "normalized": true, "fingerprint": "f1"
              }]]
            }
            """;
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse)
            : JsonResponse(HttpStatusCode.OK, queryResponse));
        var client = CreateClient(handler);

        var matches = await client.QueryAsync([0.1, 0.2], topK: 3, documentTypeFilter: null, TestContext.Current.CancellationToken);

        var match = Assert.Single(matches);
        Assert.Equal("kb:product:PRD-01", match.Id);
        Assert.Equal(0.42, match.Distance);
        Assert.Equal("rendered text", match.Document);
        Assert.Equal("PRD-01", match.Metadata.ProductCode);
    }

    // --- generic failure mapping (transport / status code / malformed body) ---

    [Fact]
    public async Task CountAsync_ServerReturns500_ThrowsRetryable()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse)
            : JsonResponse(HttpStatusCode.InternalServerError, "boom"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<KnowledgeVectorStoreException>(
            () => client.CountAsync(TestContext.Current.CancellationToken));

        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task CountAsync_ServerReturns400_ThrowsNonRetryable()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse)
            : JsonResponse(HttpStatusCode.BadRequest, "boom"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<KnowledgeVectorStoreException>(
            () => client.CountAsync(TestContext.Current.CancellationToken));

        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task CountAsync_TransportFailure_ThrowsRetryable()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse)
            : throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<KnowledgeVectorStoreException>(
            () => client.CountAsync(TestContext.Current.CancellationToken));

        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task CountAsync_MalformedBody_ThrowsNonRetryable()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/collections", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.OK, MatchingCollectionResponse)
            : JsonResponse(HttpStatusCode.OK, "{ not valid json"));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<KnowledgeVectorStoreException>(
            () => client.CountAsync(TestContext.Current.CancellationToken));

        Assert.False(exception.Retryable);
    }

    private static string EmptyGetResponse() => """{"ids": [], "metadatas": []}""";

    private static string EmptyQueryResponse() => """{"ids": [[]], "distances": [[]], "documents": [[]], "metadatas": [[]]}""";

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Path, string? Body)> _requests = [];
        public IReadOnlyList<(HttpMethod Method, string Path, string? Body)> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is not null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            _requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return respond(request);
        }
    }
}
