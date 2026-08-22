using System.Text;

namespace CrmCopilot.Tests.Web.TestSupport;

/// <summary>
/// D9(b)/(c)'s controlled test transport (per plan directive: if disposing the in-memory MCP
/// factory can't cleanly isolate a ListToolsAsync-only failure from a CallToolAsync-only failure,
/// use a controlled test transport/handler instead). Fails only the JSON-RPC request whose body
/// contains <paramref name="failOnMethodContains"/> (e.g. "tools/list" or "tools/call") — every
/// other request (including the initialize handshake) passes through to the real in-memory
/// TestServer handler untouched. This lets a single real MCP protocol round trip fail at exactly
/// one named step, deterministically, without guessing at raw HTTP request counts.
///
/// Parameterless-constructible by design (only <paramref name="failOnMethodContains"/> is
/// supplied) — WebApplicationFactory.CreateDefaultClient(params DelegatingHandler[]) wires up
/// InnerHandler itself; it must not be pre-supplied here.
/// </summary>
internal sealed class FailOnJsonRpcMethodHandler(string failOnMethodContains) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var contentType = request.Content.Headers.ContentType?.MediaType ?? "application/json";
            request.Content = new StringContent(body, Encoding.UTF8, contentType);

            if (body.Contains(failOnMethodContains, StringComparison.Ordinal))
            {
                throw new HttpRequestException(
                    $"Simulated transport failure for JSON-RPC method containing '{failOnMethodContains}' (test-controlled).");
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
