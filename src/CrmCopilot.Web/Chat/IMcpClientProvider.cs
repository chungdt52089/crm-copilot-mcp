using ModelContextProtocol.Client;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Async-safe access to a connected MCP client (plan D4). Deliberately not sync-over-async: no
/// implementation of this interface may block on GetClientAsync's own Task internally.
/// </summary>
internal interface IMcpClientProvider : IAsyncDisposable
{
    /// <summary>Throws if the MCP initialize handshake fails. A failed attempt is never cached —
    /// the next call retries (matches this project's Retryable semantics for transient upstream
    /// unavailability).</summary>
    Task<McpClient> GetClientAsync(CancellationToken cancellationToken);
}
