namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// Raised for a true Gemini generateContent call failure (transport/ClientError/ServerError) or an
/// empty/missing response.Text during generate_call_script. Deliberately has NO caller-supplied
/// message parameter — <see cref="Message"/> is always the same fixed literal, enforced by the type
/// signature itself, so no call site can pass exception-derived or otherwise unsafe text through
/// it. The caught SDK exception is attached only via <see cref="Exception.InnerException"/>, never
/// interpolated. Same contract as EmailGenerationException.
/// </summary>
internal sealed class CallScriptGenerationException : Exception
{
    private const string SafeMessage = "Không thể tạo kịch bản gọi từ Gemini.";

    public CallScriptGenerationException(bool retryable, Exception? inner = null)
        : base(SafeMessage, inner)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}
