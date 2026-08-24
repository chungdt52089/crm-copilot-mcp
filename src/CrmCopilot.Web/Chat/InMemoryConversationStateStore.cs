using System.Collections.Concurrent;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// docs/02_ARCHITECTURE.md §6: P0 implementation is a <see cref="ConcurrentDictionary{TKey,TValue}"/>;
/// restart loses all state (accepted MVP limitation). Keyed by the browser-supplied, GUID-normalized
/// sessionId (see <see cref="SessionIdValidator"/>) — never generated server-side. Registered as a
/// singleton so it survives across the per-request-scoped <see cref="ChatOrchestrator"/>.
/// </summary>
internal sealed class InMemoryConversationStateStore : IConversationStateStore
{
    private readonly ConcurrentDictionary<string, ConversationState> _states = new(StringComparer.Ordinal);

    public ConversationState GetOrCreate(string sessionId) =>
        _states.GetOrAdd(sessionId, ConversationState.CreateNew);

    public ConversationState Update(string sessionId, Func<ConversationState, ConversationState> updater) =>
        _states.AddOrUpdate(
            sessionId,
            addValueFactory: id => updater(ConversationState.CreateNew(id)),
            updateValueFactory: (_, existing) => updater(existing));

    public void Reset(string sessionId) => _states.TryRemove(sessionId, out _);
}
