using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;
using Google.GenAI;
using Microsoft.Extensions.Options;

namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// Registers IKnowledgeRetriever -&gt; KnowledgeRetriever with typed, fail-fast-on-start options
/// bound to GEMINI_API_KEY/CHROMA_BASE_URL, mirroring CrmGatewayServiceCollectionExtensions.
/// Not consumed by anything yet — registered so config fail-fast is active ahead of P0-04.
/// </summary>
public static class KnowledgeServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeRetrieval(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GeminiEmbeddingOptions>()
            .Configure(options => options.ApiKey = configuration[GeminiEmbeddingOptions.ApiKeyConfigKey] ?? string.Empty)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                $"{GeminiEmbeddingOptions.ApiKeyConfigKey} must be set.")
            .ValidateOnStart();

        services.AddOptions<ChromaOptions>()
            .Configure(options =>
            {
                options.BaseUrl = configuration[ChromaOptions.BaseUrlConfigKey] ?? string.Empty;
                options.CollectionName = configuration[ChromaOptions.CollectionNameConfigKey] is { Length: > 0 } overrideName
                    ? overrideName
                    : ChromaOptions.DefaultCollectionName;
            })
            .Validate(
                options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                $"{ChromaOptions.BaseUrlConfigKey} must be a non-empty absolute URL.")
            .ValidateOnStart();

        services.Configure<KnowledgeRetrievalOptions>(_ => { }); // defaults only — no secret, not ValidateOnStart

        services.AddSingleton(serviceProvider =>
            new Client(apiKey: serviceProvider.GetRequiredService<IOptions<GeminiEmbeddingOptions>>().Value.ApiKey));
        services.AddSingleton<IEmbeddingClient, GeminiEmbeddingClient>();

        services.AddHttpClient<IVectorStore, ChromaHttpClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ChromaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        services.AddTransient<IKnowledgeRetriever, KnowledgeRetriever>();
        services.AddTransient<KnowledgeIngestionService>();

        return services;
    }
}
