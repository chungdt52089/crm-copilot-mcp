namespace CrmCopilot.Contracts.Chat;

/// <summary>
/// P0-05 natural-language chat request (plan §7). A single free-text turn — no session/state
/// (conversation state is P0-06).
/// </summary>
public sealed record ChatRequest(string Message);
