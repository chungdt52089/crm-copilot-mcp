using Google.GenAI;
using Google.GenAI.Types;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Wraps Google.GenAI's Client.Models.GenerateContentAsync. Exception shapes mirror
/// CrmCopilot.McpServer.Knowledge.GeminiEmbeddingClient's own P0-03-verified mapping (same
/// installed Google.GenAI 1.19.0 package, same Models.* surface): HTTP-response failures surface
/// as Google.GenAI.ClientError (4xx)/Google.GenAI.ServerError (5xx), both HttpRequestException
/// subclasses exposing an int StatusCode; pure transport failures (DNS/connection) surface as a
/// plain HttpRequestException. No bare catch(Exception) anywhere in this type.
/// </summary>
internal sealed class GeminiChatClient(Client client) : IGeminiChatClient
{
    public async Task<GenerateContentResponse> GenerateAsync(
        IReadOnlyList<Content> contents, GenerateContentConfig config, CancellationToken cancellationToken)
    {
        try
        {
            return await client.Models.GenerateContentAsync(
                model: GeminiChatOptions.ModelId,
                contents: [.. contents],
                config: config,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller's own cancellation — never remapped into ChatModelException
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
    }

    /// <summary>408 (timeout) and 429 (rate limit) are the only 4xx statuses worth retrying;
    /// everything else (400 invalid argument/key, 404, etc.) means a retry would fail the same
    /// way. Same policy as GeminiEmbeddingClient.</summary>
    private static bool IsRetryableClientStatus(int statusCode) => statusCode is 408 or 429;

    internal static ChatModelException WrapFailure(bool retryable, Exception ex) =>
        new("Gemini generateContent call thất bại.", retryable, ex);
}
