using CrmCopilot.Web.Chat;

namespace CrmCopilot.Tests.Web;

/// <summary>P0-06 unit tests directly on <see cref="InMemoryConversationStateStore"/>.</summary>
public class ConversationStateStoreTests
{
    /// <summary>P0-12: the store is keyed by (userId, sessionId). These tests exercise the
    /// sessionId half, so they pin a single user.</summary>
    private const string TestUserId = "rm01";

    [Fact]
    public void GetOrCreate_UnknownSessionId_ReturnsFreshEmptyState()
    {
        var store = new InMemoryConversationStateStore();
        var sessionId = Guid.NewGuid().ToString();

        var state = store.GetOrCreate(TestUserId, sessionId);

        Assert.Equal(sessionId, state.SessionId);
        Assert.Null(state.CurrentCustomerId);
        Assert.Empty(state.RecentSanitizedMessages);
    }

    [Fact]
    public void Update_UnknownSessionId_CreatesThenAppliesUpdaterAtomically()
    {
        var store = new InMemoryConversationStateStore();
        var sessionId = Guid.NewGuid().ToString();

        var result = store.Update(TestUserId, sessionId, s => s with { CurrentCustomerId = "CUS-0001" });

        Assert.Equal("CUS-0001", result.CurrentCustomerId);
        Assert.Equal("CUS-0001", store.GetOrCreate(TestUserId, sessionId).CurrentCustomerId);
    }

    [Fact]
    public void Update_ExistingSession_SeesLatestValue()
    {
        var store = new InMemoryConversationStateStore();
        var sessionId = Guid.NewGuid().ToString();

        store.Update(TestUserId, sessionId, s => s with { CurrentCustomerId = "CUS-0001" });
        var result = store.Update(TestUserId, sessionId, s => s with { CurrentCustomerId = "CUS-0002" });

        Assert.Equal("CUS-0002", result.CurrentCustomerId);
        Assert.Equal("CUS-0002", store.GetOrCreate(TestUserId, sessionId).CurrentCustomerId);
    }

    [Fact]
    public void Reset_ThenGetOrCreate_ReturnsFreshState()
    {
        var store = new InMemoryConversationStateStore();
        var sessionId = Guid.NewGuid().ToString();
        store.Update(TestUserId, sessionId, s => s with { CurrentCustomerId = "CUS-0001" });

        store.Reset(TestUserId, sessionId);

        Assert.Null(store.GetOrCreate(TestUserId, sessionId).CurrentCustomerId);
    }

    [Fact]
    public void Reset_UnknownSessionId_IsNoOp()
    {
        var store = new InMemoryConversationStateStore();

        var exception = Record.Exception(() => store.Reset(TestUserId, Guid.NewGuid().ToString()));

        Assert.Null(exception);
    }

    [Fact]
    public void TwoDifferentSessionIds_NeverShareState()
    {
        var store = new InMemoryConversationStateStore();
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();

        store.Update(TestUserId, sessionA, s => s with { CurrentCustomerId = "CUS-0001" });

        Assert.Equal("CUS-0001", store.GetOrCreate(TestUserId, sessionA).CurrentCustomerId);
        Assert.Null(store.GetOrCreate(TestUserId, sessionB).CurrentCustomerId);
    }

    [Fact]
    public async Task Update_ConcurrentCalls_DoNotLoseWrites()
    {
        var store = new InMemoryConversationStateStore();
        var sessionId = Guid.NewGuid().ToString();
        var tags = Enumerable.Range(0, 50).Select(i => i.ToString()).ToList();

        await Task.WhenAll(tags.Select(tag => Task.Run(() =>
            store.Update(TestUserId, sessionId, s => s with { LastInteractionIds = [.. s.LastInteractionIds, tag] }))));

        var final = store.GetOrCreate(TestUserId, sessionId);
        Assert.Equal(50, final.LastInteractionIds.Count);
        Assert.Equal(50, final.LastInteractionIds.Distinct().Count());
    }
}
