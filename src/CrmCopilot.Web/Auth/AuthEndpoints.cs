using System.Security.Claims;
using System.Text;
using CrmCopilot.Contracts.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CrmCopilot.Web.Auth;

/// <summary>
/// P0-12 (WP1) cookie authentication for CrmCopilot.Web: three synthetic roles
/// (<c>RM</c>/<c>Auditor</c>/<c>Admin</c>) over data/auth/users.json. Illustrative auth on
/// synthetic data, not production-grade (docs/01 PD-020).
/// </summary>
internal static class AuthEndpoints
{
    private const string InvalidCredentialsMessage = "Tên đăng nhập hoặc mật khẩu không đúng.";

    public static IServiceCollection AddCookieAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<UserStore>();

        // P0-13. Fail fast on a missing/short key, same convention as GeminiChatOptions: HMAC-SHA256
        // rejects a key under 256 bits, and without this that surfaces only when the first chat turn
        // tries to sign — long after startup.
        services.AddOptions<McpTokenOptions>()
            .Configure(options =>
            {
                options.SigningKey = configuration[McpJwtDefaults.SigningKeyConfigKey] ?? string.Empty;

                // Development only: 5 minutes is too short to paste a token into MCP Inspector and
                // still have it alive. Production keeps the short TTL.
                options.Lifetime = environment.IsDevelopment() ? TimeSpan.FromMinutes(60) : TimeSpan.FromMinutes(5);
            })
            .Validate(
                options => Encoding.UTF8.GetByteCount(options.SigningKey) >= McpJwtDefaults.MinimumSigningKeyBytes,
                $"{McpJwtDefaults.SigningKeyConfigKey} must be set to at least {McpJwtDefaults.MinimumSigningKeyBytes} bytes.")
            .ValidateOnStart();

        services.AddSingleton<McpTokenIssuer>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "CrmCopilot.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;

                // SameAsRequest, deliberately not Always: the demo runs over http://localhost
                // (README §9A), where Always would stop the cookie from ever being set.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.LoginPath = "/Login";

                // Without this the handler answers an unauthenticated /api/chat with 302 -> the
                // login PAGE, so the browser's fetch() would receive HTML where it expects JSON.
                // Razor page requests keep the normal redirect.
                options.Events.OnRedirectToLogin = context => Respond(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => Respond(context, StatusCodes.Status403Forbidden);
            });

        services.AddAuthorization();
        return services;
    }

    private static Task Respond(RedirectContext<CookieAuthenticationOptions> context, int apiStatusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = apiStatusCode;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app, IHostEnvironment environment)
    {
        app.MapPost("/api/auth/login", LoginAsync);

        // Cast required (ASP0016): LogoutAsync's only parameter is HttpContext, so without it the
        // overload resolves to RequestDelegate and the returned IResult would be silently dropped.
        app.MapPost("/api/auth/logout", (Delegate)LogoutAsync);

        // P0-13, Development only: hands the signed-in user their own MCP bearer token so it can be
        // pasted into MCP Inspector's Authentication box. That is how the demo shows authorization
        // living at the MCP boundary — an independent client, not the Host, gets refused the same
        // way. Never mapped outside Development: it is a token-minting endpoint, and the token is
        // exactly what the MCP Server trusts.
        if (environment.IsDevelopment())
        {
            app.MapGet("/api/auth/mcp-token", McpToken).RequireAuthorization();
        }

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, UserStore userStore, HttpContext httpContext, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(AuthEndpoints).FullName!);
        var user = userStore.Validate(request.UserId, request.Password);
        if (user is null)
        {
            // userId only — never the submitted password, the stored hash, or the cookie value.
            logger.LogInformation("Login rejected for {UserId}", request.UserId);
            return TypedResults.Json(new { message = InvalidCredentialsMessage }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.UserId),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity)).ConfigureAwait(false);

        logger.LogInformation("Login succeeded for {UserId} with role {Role}", user.UserId, user.Role);
        return TypedResults.Ok(new AuthUserView(user.UserId, user.DisplayName, user.Role));
    }

    /// <summary>Idempotent: signing out when never signed in still returns 204.</summary>
    private static async Task<IResult> LogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static IResult McpToken(ClaimsPrincipal user, McpTokenIssuer tokenIssuer) =>
        tokenIssuer.Issue(user) is { Length: > 0 } token
            ? TypedResults.Ok(new { token })
            : TypedResults.Unauthorized();

    internal sealed record LoginRequest(string? UserId, string? Password);
}
