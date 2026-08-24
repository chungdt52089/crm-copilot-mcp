namespace CrmCopilot.Web.Chat;

/// <summary>
/// P0-06 conversation state storage (docs/02_ARCHITECTURE.md §6). <see cref="Update"/> is the only
/// mutation path — there is deliberately no bare "get then set" pair, since that shape has a
/// read-modify-write race under concurrent requests for the same session. <paramref name="updater"/>
/// must be a pure function of the previous state: an in-memory implementation backed by
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/> may invoke it more
/// than once under contention.
/// </summary>
internal interface IConversationStateStore
{
    ConversationState GetOrCreate(string sessionId);

    ConversationState Update(string sessionId, Func<ConversationState, ConversationState> updater);

    void Reset(string sessionId);
}
