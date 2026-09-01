using CrmCopilot.Web.Speech;

namespace CrmCopilot.Tests.Web.TestSupport;

/// <summary>
/// P0-15 offline stand-in for <see cref="ITranscriber"/>. The DI seam that keeps the endpoint suite
/// off the live Gemini model. <see cref="CallCount"/> is what proves the validation ladder rejects a
/// bad request BEFORE any model call is made.
/// </summary>
internal sealed class FakeTranscriber : ITranscriber
{
    public string Result { get; set; } = string.Empty;

    public Exception? ThrowOnTranscribe { get; set; }

    public int CallCount { get; private set; }

    public string? LastMimeType { get; private set; }

    public int? LastAudioByteCount { get; private set; }

    public Task<string> TranscribeAsync(byte[] audio, string mimeType, CancellationToken cancellationToken)
    {
        CallCount++;
        LastMimeType = mimeType;
        LastAudioByteCount = audio.Length;

        if (ThrowOnTranscribe is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(Result);
    }
}
