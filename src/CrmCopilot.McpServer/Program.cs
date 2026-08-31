using System.Diagnostics;
using CrmCopilot.McpServer.Auth;
using CrmCopilot.McpServer.CallScript;
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
builder.Services.AddCallScriptGeneration();
builder.Services.AddMcpJwtAuthentication(builder.Configuration);
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)

    // P0-13 (PD-022): one enforcement point for every tools/call, ahead of any tool body.
    // Deliberately NOT AddAuthorizationFilters() — that would also filter tools/list per role,
    // and a tool the model never sees is a tool it never calls, leaving no refusal to log.
    .WithRequestFilters(filters => filters.AddCallToolFilter(ToolAuthorizationFilter.Apply))
    .WithTools<CustomerTools>()
    .WithTools<KnowledgeTools>()
    .WithTools<EmailTools>()
    // P0-10 (plan D16): tool discovery is explicit per type — AddEmailGeneration()/
    // AddCallScriptGeneration() only wire DI and never publish a tool to tools/list.
    .WithTools<OpportunityTools>()
    .WithTools<CampaignTools>()
    .WithTools<CallScriptTools>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Deliberately anonymous — compose.yaml's healthcheck and the README preflight both probe it.
app.MapHealthChecks("/health");

// P0-13: an unauthenticated /mcp call is refused here, at the HTTP layer, with 401 — it never
// reaches a tool. FORBIDDEN (403-equivalent at the tool layer) is the different case: authenticated,
// but the role is not permitted. HttpServerSessionMode.Stateless above is unchanged.
app.MapMcp("/mcp").RequireAuthorization();

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
