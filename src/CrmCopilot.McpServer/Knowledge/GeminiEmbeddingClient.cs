using CrmCopilot.Contracts.Knowledge.Exceptions;
using Google.GenAI;
using Google.GenAI.Types;

namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Wraps Google.GenAI's Client.Models.EmbedContentAsync. Exception shapes below were confirmed
/// against the installed Google.GenAI 1.19.0 package by direct reflection + a live call with a
/// deliberately invalid API key (see the P0-03 plan §10 implementation-time gate) rather than
/// guessed from documentation: HTTP-response failures surface as Google.GenAI.ClientError (4xx)
/// or Google.GenAI.ServerError (5xx), both HttpRequestException subclasses exposing an int
/// StatusCode; pure transport failures (DNS/connection) surface as a plain HttpRequestException.
/// There is no bare `catch (Exception)` anywhere in this type.
/// </summary>
internal sealed class GeminiEmbeddingClient(Client client) : IEmbeddingClient
{
    public Task<double[]> EmbedDocumentAsync(string text, CancellationToken cancellationToken) =>
        EmbedAsync(text, EmbeddingTaskType.Document, cancellationToken);

    public Task<double[]> EmbedQueryAsync(string text, CancellationToken cancellationToken) =>
        EmbedAsync(text, EmbeddingTaskType.Query, cancellationToken);

    private async Task<double[]> EmbedAsync(string text, EmbeddingTaskType taskType, CancellationToken cancellationToken)
    {
        EmbedContentResponse response;
        try
        {
            response = await client.Models.EmbedContentAsync(
                model: GeminiEmbeddingOptions.ModelId,
                contents: text,
                config: new EmbedContentConfig
                {
                    TaskType = MapTaskType(taskType),
                    OutputDimensionality = GeminiEmbeddingOptions.Dimension,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller's own cancellation — never remapped into KnowledgeEmbeddingException
        }
        catch (ClientError ex)
        {
            throw WrapFailure(IsRetryableClientStatus(ex.StatusCode), ex);
        }
        catch (ServerError ex)
        {
            throw WrapFailure(retryable: true, ex);
        }
        catch (HttpRequestException ex)
        {
            // Pure transport failure (DNS/connection/timeout) — ClientError/ServerError above
            // already cover "server responded with an error status".
            throw WrapFailure(retryable: true, ex);
        }

        var values = response.Embeddings is { Count: > 0 } embeddings && embeddings[0].Values is { Count: > 0 } vector
            ? vector
            : throw new KnowledgeEmbeddingException("Gemini embedContent call returned no embedding values.", retryable: false);

        try
        {
            EmbeddingMath.ValidateDimension(values, GeminiEmbeddingOptions.Dimension);
        }
        catch (ArgumentException)
        {
            throw new KnowledgeEmbeddingException("Gemini embedContent call returned an unexpected embedding dimension.", retryable: false);
        }

        return EmbeddingMath.L2Normalize(values);
    }

    /// <summary>408 (timeout) and 429 (rate limit) are the only 4xx statuses worth retrying;
    /// everything else (400 invalid argument/key, 404, etc.) means a retry would fail the same
    /// way.</summary>
    private static bool IsRetryableClientStatus(int statusCode) => statusCode is 408 or 429;

    /// <summary>Message is always this fixed static string — ex (which can carry a verbose
    /// upstream error body, though never the API key: auth uses the x-goog-api-key header, not a
    /// query string) is attached only as InnerException, never interpolated into Message.
    /// internal (not private) so GeminiEmbeddingClientSecretHygieneTests can exercise it
    /// directly without needing a live/faked SDK call.</summary>
    internal static KnowledgeEmbeddingException WrapFailure(bool retryable, Exception ex) =>
        new("Gemini embedContent call failed.", retryable, ex);

    internal static string MapTaskType(EmbeddingTaskType taskType) => taskType switch
    {
        EmbeddingTaskType.Document => "RETRIEVAL_DOCUMENT",
        EmbeddingTaskType.Query => "RETRIEVAL_QUERY",
        _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, null),
    };
}
