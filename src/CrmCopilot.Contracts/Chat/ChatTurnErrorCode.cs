namespace CrmCopilot.Contracts.Chat;

/// <summary>
/// <see cref="ChatTurnError.Code"/> values producible by the P0-05 chat orchestrator (plan D9's
/// failure table plus D5/D7/D8's Host-policy codes). <c>UPSTREAM_UNAVAILABLE</c>/<c>RAG_UNAVAILABLE</c>/
/// <c>NOT_FOUND</c>/<c>INVALID_ARGUMENT</c> are pass-throughs of the underlying
/// <see cref="Mcp.McpToolErrorCode"/> value from a tool result, not re-invented here.
/// </summary>
public static class ChatTurnErrorCode
{
    // Host-side tool-selection policy (plan D5/D7/D8).
    public const string UnknownTool = "UNKNOWN_TOOL";
    public const string DuplicateToolCall = "DUPLICATE_TOOL_CALL";
    public const string MultipleFunctionCallsNotSupported = "MULTIPLE_FUNCTION_CALLS_NOT_SUPPORTED";
    public const string ToolLoopLimitExceeded = "TOOL_LOOP_LIMIT_EXCEEDED";
    public const string PiiRejected = "PII_REJECTED";
    public const string CustomerIdRequired = "CUSTOMER_ID_REQUIRED";
    public const string NameLookupNotSupported = "NAME_LOOKUP_NOT_SUPPORTED";

    // Gemini/MCP boundary failures (plan D4/D9).
    public const string ModelError = "MODEL_ERROR";
    public const string McpUnavailable = "MCP_UNAVAILABLE";
    public const string McpProtocolError = "MCP_PROTOCOL_ERROR";
    public const string McpInvalidResponse = "MCP_INVALID_RESPONSE";

    // Pass-through of a tool result's own McpToolErrorCode (plan D1).
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";
    public const string RagUnavailable = "RAG_UNAVAILABLE";
    public const string NotFound = "NOT_FOUND";

    // Input validation / catch-all.
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string InternalError = "INTERNAL_ERROR";
}
