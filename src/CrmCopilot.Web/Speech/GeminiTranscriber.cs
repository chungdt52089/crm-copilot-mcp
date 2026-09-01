using CrmCopilot.Web.Chat;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;

namespace CrmCopilot.Web.Speech;

/// <summary>
/// P0-15 (WP4). Sends the recording to Gemini as an inline audio part and returns the transcript.
///
/// Takes the <see cref="Client"/> singleton that AddChatOrchestration already registered from
/// GEMINI_API_KEY — no second client and no second key binding, the precedent set by McpServer's
/// EmailServiceCollectionExtensions. The key therefore exists only in this process; the browser
/// uploads audio to our own endpoint and never sees a credential.
/// </summary>
internal sealed class GeminiTranscriber(Client client, IOptions<SpeechOptions> options) : ITranscriber
{
    /// <summary>
    /// Asks for the transcript and nothing else. Without the "chỉ trả về" instruction the model tends
    /// to answer the audio (or describe it) rather than transcribe it, and a wrapping quote/prefix
    /// would then be pasted straight into the RM's input box.
    /// </summary>
    private const string TranscribePrompt =
        "Đây là một đoạn ghi âm ngắn bằng tiếng Việt của nhân viên ngân hàng đang nói với trợ lý CRM. " +
        "Hãy chép lại chính xác nội dung người nói thành văn bản tiếng Việt CÓ DẤU. " +
        "CHỈ trả về đúng phần văn bản đã chép: không thêm lời giải thích, không thêm dấu ngoặc kép, " +
        "không thêm tiền tố, không dịch, không tóm tắt. " +
        "Nếu không nghe được gì rõ ràng, trả về chuỗi rỗng.";

    public async Task<string> TranscribeAsync(byte[] audio, string mimeType, CancellationToken cancellationToken)
    {
        var contents = new List<Content>
        {
            new()
            {
                Role = "user",
                Parts = [Part.FromText(TranscribePrompt), Part.FromBytes(audio, mimeType)],
            },
        };

        // Transcription is a fidelity task, not a creative one — the lowest temperature the API
        // accepts. No SystemInstruction: the single user turn already carries the whole instruction.
        var config = new GenerateContentConfig { Temperature = 0.0 };

        GenerateContentResponse response;
        try
        {
            response = await client.Models.GenerateContentAsync(
                model: options.Value.ModelId,
                contents: contents,
                config: config,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller's own cancellation — never remapped
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
            // Pure transport failure. Must stay last: ClientError/ServerError both derive from it.
            throw WrapFailure(retryable: true, ex);
        }

        // A blank result is a legitimate outcome ("nghe không rõ"), not a failure — the endpoint
        // returns it as an empty transcript and the client leaves the RM's input untouched.
        return response.Text ?? string.Empty;
    }

    /// <summary>408 (timeout) and 429 (rate limit) are the only 4xx worth retrying — same policy as
    /// GeminiChatClient and GeminiEmbeddingClient.</summary>
    private static bool IsRetryableClientStatus(int statusCode) => statusCode is 408 or 429;

    /// <summary>The SDK exception is attached as InnerException only, never interpolated into the
    /// message — the secret-hygiene rule from the P0-03 review.</summary>
    private static ChatModelException WrapFailure(bool retryable, Exception ex) =>
        new("Gemini transcribe call thất bại.", retryable, ex);
}
