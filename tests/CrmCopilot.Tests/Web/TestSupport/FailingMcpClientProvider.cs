using CrmCopilot.Web.Chat;
using ModelContextProtocol.Client;

namespace CrmCopilot.Tests.Web.TestSupport;

/// <summary>
/// D9(a)-2's test seam: a synthetic IMcpClientProvider whose GetClientAsync always throws,
/// proving ChatOrchestrator's own catch-and-map-to-MCP_UNAVAILABLE logic deterministically and
/// without network flakiness (separate from McpClientProviderTests' real-provider-against-an-
/// unreachable-endpoint test, D9(a)-1).
/// </summary>
internal sealed class FailingMcpClientProvider(Exception exception) : IMcpClientProvider
{
    public Task<McpClient> GetClientAsync(CancellationToken cancellationToken) => Task.FromException<McpClient>(exception);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
