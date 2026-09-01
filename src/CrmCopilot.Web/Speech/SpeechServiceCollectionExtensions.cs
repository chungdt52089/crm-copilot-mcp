using Microsoft.AspNetCore.Http.Features;

namespace CrmCopilot.Web.Speech;

/// <summary>
/// P0-15 (WP4). Registers the transcribe feature. Deliberately registers NO Google.GenAI Client and
/// NO API-key option — GeminiTranscriber injects the Client singleton AddChatOrchestration already
/// created from GEMINI_API_KEY, the same reuse pattern as McpServer's Email/CallScript features.
/// </summary>
public static class SpeechServiceCollectionExtensions
{
    /// <summary>Headroom for the multipart boundary and part headers wrapping the file, so the
    /// handler's own 1 MB check — not the transport — is what rejects an oversized recording.</summary>
    private const long MultipartOverheadSlackBytes = 64 * 1024;

    public static IServiceCollection AddSpeechTranscription(this IServiceCollection services, IConfiguration configuration)
    {
        // Optional-key idiom, as used for ChromaOptions.CollectionName: absent falls back to the
        // spike-verified default, present-but-blank fails at startup. Deliberately NOT a required
        // variable — the model pin that matters is the const in code, and making this mandatory would
        // break every existing test host, compose file and terminal for no safety gain.
        services.AddOptions<SpeechOptions>()
            .Configure(options =>
                options.ModelId = configuration[SpeechOptions.ModelIdConfigKey] is { Length: > 0 } configured
                    ? configured
                    : SpeechOptions.DefaultModelId)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ModelId),
                $"{SpeechOptions.ModelIdConfigKey} must not be blank when it is set.")
            .ValidateOnStart();

        // WP4: "Không lưu file audio xuống đĩa". MemoryBufferThreshold defaults to 64 KB, above which
        // the multipart reader spills to a temp FILE — i.e. every recording over ~4.5 s at 14 KB/s.
        // Raising it past the cap keeps audio in memory for its whole life.
        //
        // Both limits sit deliberately ABOVE MaxAudioBytes. A multipart body is the file plus its
        // boundary and part headers, so a limit set exactly at MaxAudioBytes would make a legitimate
        // 1 MB recording overflow the transport and die as an unhandled InvalidDataException (a 500)
        // instead of reaching the handler's own size check and getting a clean 400. The slack keeps
        // the friendly rejection reachable while still bounding what is ever buffered.
        //
        // Safe to set globally: this is the app's only multipart form — login and chat both post JSON.
        const long multipartLimit = TranscribeEndpoints.MaxAudioBytes + MultipartOverheadSlackBytes;

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = multipartLimit;
            options.MemoryBufferThreshold = (int)multipartLimit;
        });

        services.AddScoped<ITranscriber, GeminiTranscriber>();

        return services;
    }
}
