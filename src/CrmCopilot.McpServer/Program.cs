using System.Diagnostics;
using CrmCopilot.McpServer.Crm;
using CrmCopilot.McpServer.Email;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

if (await TryRunKnowledgeIngestionAsync(args))
{
    return;
}

if (await TryRunKnowledgeQueryAsync(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCrmGateway(builder.Configuration);
builder.Services.AddKnowledgeRetrieval(builder.Configuration);
builder.Services.AddEmailGeneration();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<CustomerTools>()
    .WithTools<KnowledgeTools>()
    .WithTools<EmailTools>();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMcp("/mcp");

app.Run();

// --- knowledge ingestion/query CLI (dev-time only; does not start the web host) ---
// Usage: dotnet run --project src/CrmCopilot.McpServer -- --ingest-knowledge
//        dotnet run --project src/CrmCopilot.McpServer -- --query-knowledge "<text>"
// Neither verb registers AddCrmGateway — MOCKCRM_API_BASE_URL is not required to run them.
static async Task<bool> TryRunKnowledgeIngestionAsync(string[] args)
{
    if (!args.Contains("--ingest-knowledge"))
    {
        return false;
    }

    using var provider = BuildKnowledgeOnlyServiceProvider();

    var stopwatch = Stopwatch.StartNew();
    var documents = KnowledgeSourceLoader.LoadFromAppBaseDirectory();
    var ingestionService = provider.GetRequiredService<KnowledgeIngestionService>();
    var summary = await ingestionService.IngestAsync(documents, CancellationToken.None);
    var vectorStore = provider.GetRequiredService<IVectorStore>();
    var recordCount = await vectorStore.CountAsync(CancellationToken.None);
    stopwatch.Stop();

    Console.WriteLine(
        $"Ingested knowledge: {summary.TotalDocuments} documents ({summary.Embedded} embedded, {summary.Skipped} unchanged) " +
        $"model={GeminiEmbeddingOptions.ModelId} dim={GeminiEmbeddingOptions.Dimension} metric={ChromaOptions.DistanceMetric} " +
        $"collection count after ingest={recordCount} durationMs={stopwatch.ElapsedMilliseconds}");
    return true;
}

static async Task<bool> TryRunKnowledgeQueryAsync(string[] args)
{
    var queryText = ParseStringArg(args, "--query-knowledge");
    if (queryText is null)
    {
        return false;
    }

    using var provider = BuildKnowledgeOnlyServiceProvider();

    var embeddingClient = provider.GetRequiredService<IEmbeddingClient>();
    var vectorStore = provider.GetRequiredService<IVectorStore>();

    // Calls IEmbeddingClient/IVectorStore directly (not IKnowledgeRetriever) so this diagnostic
    // command can print the query embedding's own L2 norm — never the vector values themselves —
    // and every raw top-K distance for MaxDistance calibration, without a second embedding call.
    var embedding = await embeddingClient.EmbedQueryAsync(queryText, CancellationToken.None);
    var norm = Math.Sqrt(embedding.Sum(v => v * v));
    var matches = await vectorStore.QueryAsync(embedding, topK: 3, documentTypeFilter: null, CancellationToken.None);

    Console.WriteLine($"Query embedding: dimension={embedding.Length} L2 norm={norm:F6} (expect ~1.0)");
    Console.WriteLine($"Top-{matches.Count} matches (Chroma distance, metric={ChromaOptions.DistanceMetric}):");
    foreach (var match in matches)
    {
        Console.WriteLine($"  sourceId={match.Id} distance={match.Distance:F6}");
    }

    return true;
}

static ServiceProvider BuildKnowledgeOnlyServiceProvider()
{
    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
    var services = new ServiceCollection();
    services.AddKnowledgeRetrieval(configuration);

    // ASP0000 warns about BuildServiceProvider creating a second copy of the *same app's*
    // singletons alongside the real host container — not applicable here: this CLI branch
    // returns before WebApplication.CreateBuilder(args) ever runs, so there is no other
    // container in this process to duplicate.
#pragma warning disable ASP0000
    var provider = services.BuildServiceProvider();
#pragma warning restore ASP0000

    // ValidateOnStart's hosted-service trigger only fires under IHost.StartAsync(), which a bare
    // ServiceProvider never calls — touch IOptions<T>.Value explicitly so a missing
    // GEMINI_API_KEY/CHROMA_BASE_URL fails fast here, before any file I/O, same guarantee as the
    // real host gets automatically.
    _ = provider.GetRequiredService<IOptions<GeminiEmbeddingOptions>>().Value;
    _ = provider.GetRequiredService<IOptions<ChromaOptions>>().Value;

    return provider;
}

static string? ParseStringArg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
