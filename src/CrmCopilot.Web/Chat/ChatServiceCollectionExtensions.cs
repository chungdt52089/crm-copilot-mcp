using Google.GenAI;
using Microsoft.Extensions.Options;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Registers the P0-05 AI Host chat orchestration: typed, fail-fast-on-start options bound to
/// GEMINI_API_KEY/MCPSERVER_BASE_URL (mirrors CrmCopilot.McpServer.Knowledge.KnowledgeServiceCollectionExtensions'
/// GeminiEmbeddingOptions/ChromaOptions pattern), the Gemini chat client, the async MCP client
/// provider (plan D4 — no network call happens here; the MCP handshake is lazy, on first use), and
/// the orchestrator itself.
/// </summary>
public static class ChatServiceCollectionExtensions
{
    public static IServiceCollection AddChatOrchestration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GeminiChatOptions>()
            .Configure(options => options.ApiKey = configuration[GeminiChatOptions.ApiKeyConfigKey] ?? string.Empty)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                $"{GeminiChatOptions.ApiKeyConfigKey} must be set.")
            .ValidateOnStart();

        services.AddOptions<McpClientOptions>()
            .Configure(options => options.BaseUrl = configuration[McpClientOptions.BaseUrlConfigKey] ?? string.Empty)
            .Validate(
                options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                $"{McpClientOptions.BaseUrlConfigKey} must be a non-empty absolute URL.")
            .ValidateOnStart();

        services.AddSingleton(serviceProvider =>
            new Client(apiKey: serviceProvider.GetRequiredService<IOptions<GeminiChatOptions>>().Value.ApiKey));
        services.AddSingleton<IGeminiChatClient, GeminiChatClient>();

        // P0-13: SCOPED, not singleton — the transport carries the calling user's bearer token,
        // which SDK 2.2.0 fixes at transport construction. DI disposes it (IAsyncDisposable) when
        // the request scope ends, so each turn gets its own initialize + client.
        services.AddScoped<IMcpClientProvider, McpClientProvider>();

        services.AddSingleton<IConversationStateStore, InMemoryConversationStateStore>();

        services.AddScoped<ChatOrchestrator>();

        return services;
    }
}
