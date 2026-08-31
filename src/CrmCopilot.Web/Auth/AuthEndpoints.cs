using System.Security.Claims;
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

    public static IServiceCollection AddCookieAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<UserStore>();

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

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync);

        // Cast required (ASP0016): LogoutAsync's only parameter is HttpContext, so without it the
        // overload resolves to RequestDelegate and the returned IResult would be silently dropped.
        app.MapPost("/api/auth/logout", (Delegate)LogoutAsync);
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

    internal sealed record LoginRequest(string? UserId, string? Password);
}
