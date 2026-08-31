using ModelContextProtocol.Client;

namespace CrmCopilot.Tests.TestSupport;

/// <summary>
/// P0-13: the one place that builds HttpClientTransportOptions for tests, so every MCP test
/// connects the same way the production McpClientProvider does — bearer token in AdditionalHeaders
/// (the SDK pins them at construction; there is no per-request header hook) and the standalone GET
/// SSE stream disabled, which HttpServerSessionMode.Stateless does not serve anyway.
/// </summary>
internal static class McpTestTransport
{
    public static HttpClientTransportOptions Options(HttpClient httpClient, string authorizationHeader) => new()
    {
        Endpoint = new Uri(httpClient.BaseAddress!, "mcp"),
        TransportMode = HttpTransportMode.StreamableHttp,
        EnableStandaloneGetStream = false,
        AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = authorizationHeader },
    };
}
