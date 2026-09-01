namespace CrmCopilot.Contracts.Chat;

/// <summary>
/// P0-05/P0-06 natural-language chat request (plan §7; docs/02_ARCHITECTURE.md §6). A single
/// free-text turn plus the browser-generated <see cref="SessionId"/> that identifies the P0-06
/// conversation state to resolve/update — the browser creates it once (a GUID string) and resends
/// it with every request; it is never generated server-side. A missing/blank/malformed
/// <see cref="SessionId"/> is rejected with <c>INVALID_ARGUMENT</c>.
/// </summary>
public sealed record ChatRequest(string Message, string SessionId);
