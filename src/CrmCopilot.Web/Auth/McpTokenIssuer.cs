using System.Security.Claims;
using System.Text;
using CrmCopilot.Contracts.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CrmCopilot.Web.Auth;

public sealed class McpTokenOptions
{
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Short by design: the token only has to survive one chat turn, since P0-13 mints a fresh
    /// MCP client (and therefore a fresh token) per request. Development gets a longer TTL purely
    /// so a token can be pasted into MCP Inspector by hand and still be alive a minute later.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// P0-13: signs the bearer token that carries the signed-in RM's identity across the Host → MCP
/// Server boundary. HS256 over a shared symmetric secret — illustrative, matching the synthetic
/// nature of the rest of the demo, not a production key-management design.
/// </summary>
internal sealed class McpTokenIssuer(IOptions<McpTokenOptions> options)
{
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>
    /// Returns null when there is no authenticated principal — the caller then sends no
    /// Authorization header at all, and the MCP Server answers 401. Deliberately not a token with
    /// an empty subject: an unauthenticated caller must never end up holding a valid token.
    /// </summary>
    public string? Issue(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = user.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = McpJwtDefaults.Issuer,
            Audience = McpJwtDefaults.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(options.Value.Lifetime),
            SigningCredentials = _credentials,

            // Written as raw claim names, not ClaimTypes.* URIs: the MCP Server validates with
            // MapInboundClaims = false, so what is written here is exactly what it reads back.
            Claims = new Dictionary<string, object>
            {
                [McpJwtDefaults.UserIdClaim] = userId,
                [McpJwtDefaults.RoleClaim] = role,
            },
        });
    }
}
