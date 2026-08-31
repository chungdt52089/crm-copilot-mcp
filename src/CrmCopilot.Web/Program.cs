using CrmCopilot.Web.Auth;
using CrmCopilot.Web.Chat;
using Microsoft.AspNetCore.Identity;

// Dev-time only, does not start the web host. Generates one PasswordHasher<T> v3 hash to paste
// into data/auth/users.json, so no plaintext password is ever stored. Same shape as
// CrmCopilot.McpServer's --ingest-knowledge branch: it returns before CreateBuilder runs, and
// WebApplicationFactory invokes the entry point with no args, so tests never reach it.
// Usage: dotnet run --project src/CrmCopilot.Web -- --hash-password "<password>"
if (args is ["--hash-password", var plainPassword])
{
    Console.WriteLine(new PasswordHasher<AuthUser>().HashPassword(null!, plainPassword));
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCookieAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddChatOrchestration(builder.Configuration);
builder.Services.AddRazorPages();

var app = builder.Build();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Deliberately anonymous — compose.yaml's healthcheck and the README preflight both probe it.
app.MapHealthChecks("/health");
app.MapAuthEndpoints(app.Environment);
app.MapChatEndpoints();
app.MapRazorPages();

app.Run();
