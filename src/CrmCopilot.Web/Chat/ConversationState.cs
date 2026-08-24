namespace CrmCopilot.Web.Chat;

/// <summary>
/// P0-06 short-lived, Host-owned conversation memory (docs/02_ARCHITECTURE.md §6) — NOT a
/// transcript store. <see cref="CurrentOpportunityId"/> is reserved for a post-MVP (P1)
/// opportunity feature and <see cref="PendingEmailDraftId"/> for P0-07's email draft; both stay
/// <c>null</c> until those checkpoints add a producer. <see cref="RecentSanitizedMessages"/> holds
/// at most the newest 8 entries, each already redacted by <see cref="ConversationMessageSanitizer"/>
/// before being stored.
/// </summary>
internal sealed record ConversationState(
    string SessionId,
    string? CurrentCustomerId,
    string? CurrentOpportunityId,
    string? LastIntent,
    IReadOnlyList<string> LastInteractionIds,
    IReadOnlyList<string> RetrievedSourceIds,
    string? PendingEmailDraftId,
    IReadOnlyList<string> RecentSanitizedMessages,
    DateTime UpdatedAtUtc)
{
    public static ConversationState CreateNew(string sessionId) =>
        new(sessionId, null, null, null, [], [], null, [], DateTime.UtcNow);
}
