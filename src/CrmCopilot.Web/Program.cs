using CrmCopilot.Web.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddChatOrchestration(builder.Configuration);
builder.Services.AddRazorPages();

var app = builder.Build();

app.UseStaticFiles();

app.MapHealthChecks("/health");
app.MapChatEndpoints();
app.MapRazorPages();

app.Run();
