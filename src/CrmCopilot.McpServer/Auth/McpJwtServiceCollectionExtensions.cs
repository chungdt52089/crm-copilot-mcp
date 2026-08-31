using System.Text;
using CrmCopilot.Contracts.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CrmCopilot.McpServer.Auth;

public sealed class McpJwtOptions
{
    public string SigningKey { get; set; } = string.Empty;
}

/// <summary>
/// P0-13: validates the bearer token CrmCopilot.Web mints (see Web/Auth/McpTokenIssuer.cs) using
/// the same shared symmetric secret from MCP_JWT_SIGNING_KEY. Fail-fast on a missing/short key via
/// ValidateOnStart, mirroring the GeminiEmbeddingOptions/ChromaOptions convention.
///
/// The key is read through the options pipeline rather than straight off IConfiguration here,
/// and that is load-bearing, not style: this method runs during service registration, whereas
/// WebApplicationFactory appends its AddInMemoryCollection source afterwards (see
/// McpServerTestHost's class doc). An eager read at registration time sees configuration before
/// the test host's values exist, so every in-memory McpServer host fails to start.
/// </summary>
internal static class McpJwtServiceCollectionExtensions
{
    public static IServiceCollection AddMcpJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<McpJwtOptions>()
            .Configure(options => options.SigningKey = configuration[McpJwtDefaults.SigningKeyConfigKey] ?? string.Empty)
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.SigningKey) >= McpJwtDefaults.MinimumSigningKeyBytes,
                $"{McpJwtDefaults.SigningKeyConfigKey} must be set to at least {McpJwtDefaults.MinimumSigningKeyBytes} bytes.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured with a dependency on McpJwtOptions so the key is resolved when the handler
        // first needs it — after configuration is fully composed.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<McpJwtOptions>>((options, mcpJwtOptions) =>
            {
                // Off on purpose: with mapping on, "role"/"sub" are rewritten to ClaimTypes.* URIs
                // and ToolAuthorizationFilter's lookups by literal name would silently find nothing
                // — which fails closed, but for the wrong reason and invisibly.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = McpJwtDefaults.Issuer,
                    ValidateAudience = true,
                    ValidAudience = McpJwtDefaults.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(mcpJwtOptions.Value.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = McpJwtDefaults.UserIdClaim,
                    RoleClaimType = McpJwtDefaults.RoleClaim,
                };
            });

        services.AddAuthorization();
        return services;
    }
}
