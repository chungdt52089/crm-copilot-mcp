using System.Runtime.CompilerServices;
using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.MockCrmApi.Data;
using CrmCopilot.MockCrmApi.Data.Generation;
using CrmCopilot.MockCrmApi.Endpoints;
using CrmCopilot.MockCrmApi.ErrorHandling;

if (TryRunDatasetGeneration(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddSingleton(_ => CrmDatasetLoader.LoadFromAppBaseDirectory());
builder.Services.AddSingleton<SoftDeleteRegistry>();
builder.Services.AddExceptionHandler<InternalErrorHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Force the dataset to load now, so a bad dataset fails startup instead of the first request.
app.Services.GetRequiredService<CrmDataset>();

app.UseExceptionHandler();

app.MapHealthChecks("/health");
app.MapCustomerEndpoints();

app.Run();

// --- data/crm regeneration CLI (dev-time only; does not start the web host) ---
// Usage: dotnet run --project src/CrmCopilot.MockCrmApi -- --generate-dataset [--customers N] [--seed N] [--output <dir>]
static bool TryRunDatasetGeneration(string[] args)
{
    if (!args.Contains("--generate-dataset"))
    {
        return false;
    }

    var seed = ParseIntArg(args, "--seed") ?? DatasetGenerationOptions.DefaultSeed;
    var customerCount = ParseIntArg(args, "--customers") ?? DatasetGenerationOptions.DefaultCustomerCount;
    var outputDirectory = ParseStringArg(args, "--output") ?? DefaultDatasetOutputDirectory();

    var options = new DatasetGenerationOptions(seed, customerCount);
    var (customers, interactions) = SyntheticDatasetGenerator.Generate(options);

    // P0-10: opportunities/campaigns come from a separate, Random-free generator that only reads
    // the already-generated customers — so regenerating them can never perturb the customer/
    // interaction byte output or its checked-in hashes (plan D9).
    var (opportunities, campaigns) = SyntheticRelationshipDatasetGenerator.Generate(options, customers);

    Directory.CreateDirectory(outputDirectory);
    File.WriteAllText(Path.Combine(outputDirectory, "customers.json"), JsonSerializer.Serialize(customers, CrmJsonOptions.Indented));
    File.WriteAllText(Path.Combine(outputDirectory, "interactions.json"), JsonSerializer.Serialize(interactions, CrmJsonOptions.Indented));
    File.WriteAllText(Path.Combine(outputDirectory, "opportunities.json"), JsonSerializer.Serialize(opportunities, CrmJsonOptions.Indented));
    File.WriteAllText(Path.Combine(outputDirectory, "campaigns.json"), JsonSerializer.Serialize(campaigns, CrmJsonOptions.Indented));

    Console.WriteLine(
        $"Generated {customers.Count} customers / {interactions.Count} interactions / " +
        $"{opportunities.Count} opportunities / {campaigns.Count} campaigns (seed={seed}) into {outputDirectory}");
    return true;
}

// Resolved once at compile time to this source file's own location, then navigated to
// <repo>/data/crm — independent of the process's current working directory at run time.
static string DefaultDatasetOutputDirectory([CallerFilePath] string sourceFile = "") =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "data", "crm"));

static int? ParseIntArg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : null;
}

static string? ParseStringArg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
