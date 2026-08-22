using CrmCopilot.Web.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddChatOrchestration(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapChatEndpoints();

app.Run();
