using System.Security.Claims;
using CrmCopilot.Contracts.Auth;
using CrmCopilot.Web.Auth;
using Microsoft.Extensions.Options;

namespace CrmCopilot.Tests.TestSupport;

/// <summary>
/// P0-13: mints the bearer token that MCP-level tests present to the now-authorized /mcp endpoint.
/// Deliberately goes through the production <see cref="McpTokenIssuer"/> rather than hand-rolling a
/// JWT, so a token shape the issuer could not actually produce can never make a test pass.
///
/// Defaults to <see cref="Roles.Admin"/> so every pre-P0-13 test keeps exercising exactly the tool
/// set it did before — authorization behaviour itself is verified from the browser/Inspector, not
/// here (this checkpoint adds no new tests).
/// </summary>
internal static class McpTestTokens
{
    /// <summary>Any value works as long as it is >= 32 bytes (HMAC-SHA256). Not a secret: it only
    /// ever signs tokens for an in-memory test host.</summary>
    public const string SigningKey = "test-mcp-jwt-signing-key-32-bytes-minimum!!";

    private static readonly McpTokenIssuer Issuer = new(
        Options.Create(new McpTokenOptions { SigningKey = SigningKey, Lifetime = TimeSpan.FromMinutes(10) }));

    public static string ForRole(string role = Roles.Admin, string userId = "admin01") =>
        Issuer.Issue(Principal(role, userId))
        ?? throw new InvalidOperationException($"Test token issuance failed for role '{role}'.");

    public static string AuthorizationHeader(string role = Roles.Admin, string userId = "admin01") =>
        $"Bearer {ForRole(role, userId)}";

    private static ClaimsPrincipal Principal(string role, string userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)],
            authenticationType: "TestAuth"));
}
