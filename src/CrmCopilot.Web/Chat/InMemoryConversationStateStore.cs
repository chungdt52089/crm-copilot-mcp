using System.Collections.Concurrent;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// docs/02_ARCHITECTURE.md §6: P0 implementation is a <see cref="ConcurrentDictionary{TKey,TValue}"/>;
/// restart loses all state (accepted MVP limitation). Keyed by the authenticated userId plus the
/// browser-supplied, GUID-normalized sessionId (see <see cref="SessionIdValidator"/>) — the
/// sessionId half is never generated server-side. Registered as a singleton so it survives across
/// the per-request-scoped <see cref="ChatOrchestrator"/>.
///
/// The ValueTuple key compares both halves with <see cref="EqualityComparer{T}.Default"/>, which
/// for string is ordinal — the same comparison the single-key version used explicitly.
/// </summary>
internal sealed class InMemoryConversationStateStore : IConversationStateStore
{
    private readonly ConcurrentDictionary<(string UserId, string SessionId), ConversationState> _states = new();

    public ConversationState GetOrCreate(string userId, string sessionId) =>
        _states.GetOrAdd((userId, sessionId), key => ConversationState.CreateNew(key.SessionId));

    public ConversationState Update(string userId, string sessionId, Func<ConversationState, ConversationState> updater) =>
        _states.AddOrUpdate(
            (userId, sessionId),
            addValueFactory: key => updater(ConversationState.CreateNew(key.SessionId)),
            updateValueFactory: (_, existing) => updater(existing));

    public void Reset(string userId, string sessionId) => _states.TryRemove((userId, sessionId), out _);
}
