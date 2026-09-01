namespace CrmCopilot.Web.Speech;

/// <summary>
/// Wraps a single Gemini transcription call. Exists as an interface for the same reason
/// <see cref="CrmCopilot.Web.Chat.IGeminiChatClient"/> does: it is the DI seam every offline endpoint
/// test overrides, so the test suite never reaches the live model.
/// </summary>
internal interface ITranscriber
{
    /// <summary>
    /// Returns the raw transcript as the model produced it — normalization is the endpoint's job, not
    /// this one's, so both steps stay independently testable.
    ///
    /// Throws <see cref="CrmCopilot.Web.Chat.ChatModelException"/> for any transcription failure.
    /// </summary>
    /// <param name="mimeType">A bare IANA type such as <c>audio/webm</c>, with no parameters.</param>
    Task<string> TranscribeAsync(byte[] audio, string mimeType, CancellationToken cancellationToken);
}
