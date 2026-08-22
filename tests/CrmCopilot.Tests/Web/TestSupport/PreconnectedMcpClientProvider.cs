using CrmCopilot.Web.Chat;
using ModelContextProtocol.Client;

namespace CrmCopilot.Tests.Web.TestSupport;

/// <summary>
/// D4's test seam: wraps an already-connected REAL McpClient (built exactly like
/// McpToolProtocolTests.ConnectAsync — HttpClientTransport over an in-memory McpServerTestHost's
/// HttpClient), so ListToolsAsync/CallToolAsync are still exercised over the real MCP protocol in
/// every integration test — only "how is the client obtained" is swapped. Does not own/dispose the
/// wrapped client — the test that created it controls its lifecycle.
/// </summary>
internal sealed class PreconnectedMcpClientProvider(McpClient client) : IMcpClientProvider
{
    public Task<McpClient> GetClientAsync(CancellationToken cancellationToken) => Task.FromResult(client);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
