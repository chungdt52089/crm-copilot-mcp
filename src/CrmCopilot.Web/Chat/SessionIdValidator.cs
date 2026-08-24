namespace CrmCopilot.Web.Chat;

/// <summary>
/// Shared session ID validation/normalization for both <c>POST /api/chat</c> and
/// <c>DELETE /api/chat/sessions/{sessionId}</c> (P0-06) — a single helper so a GUID with a
/// different-but-equal textual form (e.g. braces) can't resolve to two different sessions
/// depending on which endpoint parsed it.
/// </summary>
internal static class SessionIdValidator
{
    public const string InvalidSessionIdMessage = "SessionId phải là một GUID hợp lệ.";

    public static bool TryNormalize(string? sessionId, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId) || !Guid.TryParse(sessionId, out var guid))
        {
            return false;
        }

        normalized = guid.ToString();
        return true;
    }
}
