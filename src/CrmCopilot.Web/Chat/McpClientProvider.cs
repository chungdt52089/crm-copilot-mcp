using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Lazy, thread-safe, async MCP client provider (plan D4). No .GetAwaiter().GetResult() anywhere —
/// the real MCP `initialize` handshake happens on first GetClientAsync call (first chat request),
/// not at Program.cs top level before builder.Build(). A failed CreateAsync does NOT cache a
/// permanent failure: _client stays null, so the next request retries the handshake.
/// </summary>
internal sealed class McpClientProvider(IOptions<McpClientOptions> options) : IMcpClientProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;

    public async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is { } existing)
        {
            return existing;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check inside the gate — a concurrent request may have already created it.
            _client ??= await CreateAsync(cancellationToken).ConfigureAwait(false);
            return _client;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<McpClient> CreateAsync(CancellationToken cancellationToken)
    {
        var baseUrl = options.Value.BaseUrl;
        var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is { } existing)
        {
            await existing.DisposeAsync().ConfigureAwait(false);
        }
    }
}
