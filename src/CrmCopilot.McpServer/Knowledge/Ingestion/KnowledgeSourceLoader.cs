using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.McpServer.Knowledge.Ingestion;

/// <summary>
/// Loads and validates data/knowledge/*.json at ingestion time — fails fast on any structural
/// problem, mirroring CrmCopilot.MockCrmApi.Data.CrmDatasetLoader. Path is resolved via
/// AppContext.BaseDirectory, not the process's current working directory, so behavior is
/// identical under `dotnet run` and the test host — the files are copied into
/// CrmCopilot.McpServer's own build output (see CrmCopilot.McpServer.csproj's
/// CopyToOutputDirectory item).
///
/// Renders one deterministic text per document from a fixed field allowlist — no chunking
/// (docs/08_RAG_EMAIL_AND_PII_SPEC.md §3: P0 uses one vector/record per document).
/// </summary>
internal static class KnowledgeSourceLoader
{
    public static IReadOnlyList<KnowledgeSourceDocument> LoadFromAppBaseDirectory() =>
        LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "knowledge"));

    public static IReadOnlyList<KnowledgeSourceDocument> LoadFromDirectory(string directory)
    {
        var products = ReadJsonFile<List<ProductKnowledgeDto>>(Path.Combine(directory, "products.json"));
        var templates = ReadJsonFile<List<EmailTemplateKnowledgeDto>>(Path.Combine(directory, "email-templates.json"));

        Validate(products, templates);

        var documents = new List<KnowledgeSourceDocument>(products.Count + templates.Count);
        documents.AddRange(products.Select(RenderProduct));
        documents.AddRange(templates.Select(RenderTemplate));
        return documents;
    }

    internal static void Validate(IReadOnlyList<ProductKnowledgeDto> products, IReadOnlyList<EmailTemplateKnowledgeDto> templates)
    {
        var errors = new List<string>();
        var sourceIds = new HashSet<string>();

        foreach (var product in products)
        {
            if (!product.SourceId.StartsWith("kb:product:", StringComparison.Ordinal))
            {
                errors.Add($"Product sourceId '{product.SourceId}' does not use the kb:product: prefix.");
            }

            if (!sourceIds.Add(product.SourceId))
            {
                errors.Add($"Duplicate sourceId '{product.SourceId}'.");
            }

            if (!product.Synthetic)
            {
                errors.Add($"Product '{product.SourceId}' is not marked synthetic.");
            }

            if (product.Language != "vi")
            {
                errors.Add($"Product '{product.SourceId}' language is not 'vi'.");
            }

            if (string.IsNullOrWhiteSpace(product.ProductCode))
            {
                errors.Add($"Product '{product.SourceId}' has an empty productCode.");
            }
        }

        foreach (var template in templates)
        {
            if (!template.SourceId.StartsWith("kb:email-template:", StringComparison.Ordinal))
            {
                errors.Add($"Template sourceId '{template.SourceId}' does not use the kb:email-template: prefix.");
            }

            if (!sourceIds.Add(template.SourceId))
            {
                errors.Add($"Duplicate sourceId '{template.SourceId}'.");
            }

            if (!template.Synthetic)
            {
                errors.Add($"Template '{template.SourceId}' is not marked synthetic.");
            }

            if (template.Language != "vi")
            {
                errors.Add($"Template '{template.SourceId}' language is not 'vi'.");
            }

            if (string.IsNullOrWhiteSpace(template.TemplateId))
            {
                errors.Add($"Template '{template.SourceId}' has an empty templateId.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Knowledge dataset failed validation:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    private static KnowledgeSourceDocument RenderProduct(ProductKnowledgeDto product) => new(
        product.SourceId,
        KnowledgeDocumentType.Product,
        $"{product.Name}\n{product.Summary}\nĐối tượng phù hợp: {string.Join("; ", product.Eligibility)}\nLợi ích: {string.Join("; ", product.Benefits)}\nĐiều kiện: {string.Join("; ", product.Constraints)}",
        product.ProductCode,
        null,
        product.Language,
        product.Version);

    private static KnowledgeSourceDocument RenderTemplate(EmailTemplateKnowledgeDto template) => new(
        template.SourceId,
        KnowledgeDocumentType.EmailTemplate,
        $"{template.Intent}\n{template.Tone}\n{template.SubjectPattern}\n{string.Join("\n", template.BodyGuidance)}",
        null,
        template.TemplateId,
        template.Language,
        template.Version);

    private static T ReadJsonFile<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Knowledge dataset file not found: {path}");
        }

        var json = File.ReadAllText(path);

        try
        {
            return JsonSerializer.Deserialize<T>(json, CrmJsonOptions.Default)
                ?? throw new InvalidOperationException($"Knowledge dataset file '{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Knowledge dataset file '{path}' is not valid JSON.", ex);
        }
    }
}
