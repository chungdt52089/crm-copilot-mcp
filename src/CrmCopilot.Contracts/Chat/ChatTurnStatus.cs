namespace CrmCopilot.Contracts.Chat;

/// <summary>
/// <see cref="ChatResponse.Status"/> values (plan §7). Const-string class, mirrors the existing
/// wire-value convention used by <see cref="Mcp.McpToolStatus"/>/<c>ApiErrorCode</c> instead of an
/// enum-to-JSON-converter.
/// </summary>
public static class ChatTurnStatus
{
    public const string Success = "success";
    public const string NotFound = "not_found";
    public const string Ambiguous = "ambiguous";
    public const string Error = "error";
}
