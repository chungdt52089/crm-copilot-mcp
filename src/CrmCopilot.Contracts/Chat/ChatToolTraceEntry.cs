namespace CrmCopilot.Contracts.Chat;

/// <summary>
/// One MCP tool call made during a chat turn, in call order (plan §7). <see cref="Status"/> is the
/// tool result's own <see cref="Mcp.McpToolStatus"/> value; <see cref="TraceId"/> is the MCP tool's
/// own trace id, not a Host-generated one.
/// </summary>
public sealed record ChatToolTraceEntry(string ToolName, string Status, string TraceId, long DurationMs);
