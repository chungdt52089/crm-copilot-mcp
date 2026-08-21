namespace CrmCopilot.Contracts.Mcp;

/// <summary>
/// Error detail for <see cref="McpToolResult"/> (docs/07_MCP_TOOL_CONTRACTS.md §3). Message is
/// always a fixed, safe, Vietnamese description — never a caught exception's raw
/// Message/ToString(), per the secret-hygiene rule established during the P0-03 review.
/// </summary>
public sealed record McpToolError(string Code, string Message, bool Retryable);
