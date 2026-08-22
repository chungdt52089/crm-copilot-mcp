namespace CrmCopilot.Contracts.Chat;

/// <summary>
/// P0-05 chat turn result envelope (plan §7). <see cref="Reply"/> is Gemini's final grounded text
/// (only ever populated on <see cref="ChatTurnStatus.Success"/> — every other status is rendered
/// deterministically by the Host, per plan D1, and never carries model-generated text).
/// </summary>
public sealed record ChatResponse(
    string? Reply,
    string Status,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<ChatToolTraceEntry> ToolTrace,
    ChatResponseData? Data,
    ChatTurnError? Error);
