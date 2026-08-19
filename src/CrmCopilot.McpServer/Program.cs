using CrmCopilot.McpServer.Crm;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddCrmGateway(builder.Configuration);

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
