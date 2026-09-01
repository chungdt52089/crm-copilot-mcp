using CrmCopilot.Web.Auth;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Lazy, thread-safe, async MCP client provider (plan D4). No .GetAwaiter().GetResult() anywhere —
/// the real MCP `initialize` handshake happens on first GetClientAsync call (first chat request),
/// not at Program.cs top level before builder.Build(). A failed CreateAsync does NOT cache a
/// permanent failure: _client stays null, so the next request retries the handshake.
///
/// P0-13: registered SCOPED, not singleton. The transport carries one user's bearer token, and
/// ModelContextProtocol 2.2.0's HttpClientTransportOptions.AdditionalHeaders is fixed when the
/// transport is built — there is no per-request header hook (RequestOptions carries only
/// JsonSerializerOptions/Meta/ProgressToken). So a shared client would pin whichever user happened
/// to connect first. One client per request also removes every "runs outside a request context"
/// hazard: the standalone GET SSE stream is disabled below, and the session DELETE on dispose
/// happens while the request scope is still alive.
/// </summary>
internal sealed class McpClientProvider(
    IOptions<McpClientOptions> options,
    McpTokenIssuer tokenIssuer,
    IHttpContextAccessor httpContextAccessor) : IMcpClientProvider
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

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(httpClient.BaseAddress!, "mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,

            // The MCP Server runs HttpServerSessionMode.Stateless, where the standalone GET/SSE
            // endpoint does not exist at all. Leaving this at its default (true) would have the
            // transport open a background GET outside any request context — which, now that /mcp
            // requires authorization, is a request with no way to obtain a token.
            EnableStandaloneGetStream = false,
        };

        // No authenticated user (or no role claim) => no header at all, and the MCP Server answers
        // 401. Deliberately not a placeholder token: an unauthenticated caller must never hold one.
        if (tokenIssuer.Issue(httpContextAccessor.HttpContext?.User) is { Length: > 0 } token)
        {
            transportOptions.AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}",
            };
        }

        var transport = new HttpClientTransport(transportOptions, httpClient, ownsHttpClient: true);

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
