namespace CrmCopilot.Contracts.Chat;

/// <summary>
/// Error detail for <see cref="ChatResponse"/> (plan D9). Message is always a fixed, safe,
/// Vietnamese description — never a caught exception's raw Message/ToString(), same secret-hygiene
/// rule as <see cref="Mcp.McpToolError"/>.
/// </summary>
public sealed record ChatTurnError(string Code, string Message, bool Retryable);
