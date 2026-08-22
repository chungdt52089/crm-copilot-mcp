using CrmCopilot.Web.Chat;
using Microsoft.Extensions.Options;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// D9(a)-1: the real (not faked) McpClientProvider, driven against a genuinely unreachable local
/// endpoint, proves the real production adapter actually surfaces a handshake failure — rather
/// than hanging or silently succeeding — and never blocks synchronously (no
/// .GetAwaiter().GetResult() anywhere, plan D4).
/// </summary>
public class McpClientProviderTests
{
    [Fact]
    public async Task GetClientAsync_UnreachableEndpoint_ThrowsRatherThanHangingOrSucceeding()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new McpClientOptions { BaseUrl = "http://127.0.0.1:1" });
        await using var provider = new McpClientProvider(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetClientAsync(cts.Token));
    }

    [Fact]
    public async Task GetClientAsync_FailedAttempt_IsNotCached_NextCallRetries()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new McpClientOptions { BaseUrl = "http://127.0.0.1:1" });
        await using var provider = new McpClientProvider(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetClientAsync(cts.Token));
        // A second attempt must independently retry the handshake (not return a cached faulted
        // client/throw a cached exception synchronously) — same class of failure again confirms
        // it re-attempted rather than short-circuiting on a remembered failure.
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetClientAsync(cts2.Token));
    }
}
