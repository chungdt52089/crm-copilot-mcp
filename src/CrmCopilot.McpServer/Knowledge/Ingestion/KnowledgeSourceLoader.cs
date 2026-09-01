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
        var callScripts = ReadJsonFile<List<CallScriptKnowledgeDto>>(Path.Combine(directory, "call-scripts.json"));

        Validate(products, templates, callScripts);

        var documents = new List<KnowledgeSourceDocument>(products.Count + templates.Count + callScripts.Count);
        documents.AddRange(products.Select(RenderProduct));
        documents.AddRange(templates.Select(RenderTemplate));
        documents.AddRange(callScripts.Select(RenderCallScript));
        return documents;
    }

    internal static void Validate(
        IReadOnlyList<ProductKnowledgeDto> products,
        IReadOnlyList<EmailTemplateKnowledgeDto> templates,
        IReadOnlyList<CallScriptKnowledgeDto> callScripts)
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

        foreach (var callScript in callScripts)
        {
            if (!callScript.SourceId.StartsWith("kb:call-script:", StringComparison.Ordinal))
            {
                errors.Add($"Call script sourceId '{callScript.SourceId}' does not use the kb:call-script: prefix.");
            }

            if (!sourceIds.Add(callScript.SourceId))
            {
                errors.Add($"Duplicate sourceId '{callScript.SourceId}'.");
            }

            if (!callScript.Synthetic)
            {
                errors.Add($"Call script '{callScript.SourceId}' is not marked synthetic.");
            }

            if (callScript.Language != "vi")
            {
                errors.Add($"Call script '{callScript.SourceId}' language is not 'vi'.");
            }

            if (string.IsNullOrWhiteSpace(callScript.ScriptId))
            {
                errors.Add($"Call script '{callScript.SourceId}' has an empty scriptId.");
            }

            // A script whose guidance sections are empty would still embed and still retrieve, then
            // silently give the generator nothing to ground the corresponding output section on.
            if (string.IsNullOrWhiteSpace(callScript.OpeningGuidance) || string.IsNullOrWhiteSpace(callScript.ClosingGuidance))
            {
                errors.Add($"Call script '{callScript.SourceId}' has empty opening or closing guidance.");
            }

            if (callScript.DiscoveryQuestionGuidance.Count == 0 || callScript.TalkingPointGuidance.Count == 0)
            {
                errors.Add($"Call script '{callScript.SourceId}' has no discovery-question or talking-point guidance.");
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

    /// <summary>
    /// P0-10. The scriptId is carried in the metadata's TemplateId slot deliberately: it is the
    /// existing "non-product identifier" field, so call scripts index into the current Chroma
    /// metadata schema unchanged. Adding a separate scriptId column would be a vector-store schema
    /// change requiring a full re-index, which this checkpoint explicitly avoids (plan D8).
    /// </summary>
    private static KnowledgeSourceDocument RenderCallScript(CallScriptKnowledgeDto callScript) => new(
        callScript.SourceId,
        KnowledgeDocumentType.CallScript,
        $"{callScript.Intent}\n{callScript.Tone}\n{callScript.OpeningGuidance}\n" +
        $"{string.Join("\n", callScript.DiscoveryQuestionGuidance)}\n" +
        $"{string.Join("\n", callScript.TalkingPointGuidance)}\n" +
        $"{string.Join("\n", callScript.ObjectionHandlingGuidance)}\n{callScript.ClosingGuidance}",
        null,
        callScript.ScriptId,
        callScript.Language,
        callScript.Version);

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
