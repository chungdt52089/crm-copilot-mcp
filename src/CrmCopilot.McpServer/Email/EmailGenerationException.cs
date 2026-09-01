namespace CrmCopilot.McpServer.Email;

/// <summary>
/// Raised for a true Gemini generateContent call failure (transport/ClientError/ServerError) or an
/// empty/missing response.Text during generate_email. Deliberately has NO caller-supplied
/// <c>message</c> constructor parameter — <see cref="Message"/> is always the same fixed literal,
/// enforced by the type signature itself, so no call site can ever pass exception-derived or
/// otherwise unsafe text through it (P0-07 plan Amendment/Revision 2 ✏️27). The caught SDK
/// exception is attached only via <see cref="Exception.InnerException"/>, never interpolated.
/// </summary>
internal sealed class EmailGenerationException : Exception
{
    private const string SafeMessage = "Không thể tạo email draft từ Gemini.";

    public EmailGenerationException(bool retryable, Exception? inner = null)
        : base(SafeMessage, inner)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}
