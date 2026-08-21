namespace CrmCopilot.Contracts.Mcp;

/// <summary>
/// <see cref="McpToolResult.Status"/> values (docs/07_MCP_TOOL_CONTRACTS.md §3). A const-string
/// class, not an enum — mirrors this project's existing wire-value convention (e.g.
/// CrmCopilot.Contracts.Api.ApiErrorCode, CrmCopilot.McpServer.Knowledge.KnowledgeDocumentTypeWireFormat)
/// instead of introducing enum-to-JSON-converter machinery.
/// </summary>
public static class McpToolStatus
{
    public const string Success = "success";
    public const string NotFound = "not_found";
    public const string Ambiguous = "ambiguous";
    public const string Error = "error";
}
