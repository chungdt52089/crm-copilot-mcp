namespace CrmCopilot.Web.Speech;

/// <summary>
/// P0-15 (WP4). Kept as a plain class (not a record) for the same reason as
/// <see cref="CrmCopilot.Web.Chat.GeminiChatOptions"/>: a record's generated ToString() would print
/// every property, and this type sits beside ones that hold secrets.
///
/// Only the MODEL id is configurable here. The API key is deliberately NOT duplicated — the
/// transcriber reuses the Google.GenAI Client singleton already registered from GEMINI_API_KEY by
/// AddChatOrchestration, exactly as McpServer's Email/CallScript features reuse theirs.
/// </summary>
public sealed class SpeechOptions
{
    public const string ModelIdConfigKey = "SPEECH_MODEL_ID";

    /// <summary>
    /// Spike A (docs/15 §WP4, 2026-08-27) measured this directly: gemini-3.5-flash-lite — the pin used
    /// for chat/email/call-script — returns garbage for audio ("Hải Phòng", "vợ"), while the full
    /// gemini-3.5-flash transcribes correctly. This model is therefore transcribe-only and must never
    /// be conflated with GeminiChatOptions.ModelId.
    /// </summary>
    public const string DefaultModelId = "gemini-3.5-flash";

    public string ModelId { get; set; } = DefaultModelId;
}
