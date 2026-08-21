using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;

namespace CrmCopilot.Tests.Knowledge;

public class KnowledgeSourceLoaderTests
{
    [Fact]
    public void LoadFromAppBaseDirectory_CalledTwice_ProducesByteIdenticalRenderedText()
    {
        var first = KnowledgeSourceLoader.LoadFromAppBaseDirectory().ToDictionary(d => d.SourceId, d => d.RenderedText);
        var second = KnowledgeSourceLoader.LoadFromAppBaseDirectory().ToDictionary(d => d.SourceId, d => d.RenderedText);

        Assert.Equal(first.Count, second.Count);
        Assert.All(first, kv => Assert.Equal(kv.Value, second[kv.Key]));
    }

    [Fact]
    public void LoadFromDirectory_RendersFieldsAndPreservesMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "crmcopilot-knowledge-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "products.json"), """
                [{
                  "sourceId": "kb:product:PRD-TEST-01",
                  "productCode": "PRD-TEST-01",
                  "name": "Test Product",
                  "category": "Savings",
                  "summary": "Summary text",
                  "eligibility": ["Eligibility A"],
                  "benefits": ["Benefit A"],
                  "constraints": ["Constraint A"],
                  "language": "vi",
                  "synthetic": true,
                  "version": "1.0"
                }]
                """);
            File.WriteAllText(Path.Combine(directory, "email-templates.json"), """
                [{
                  "sourceId": "kb:email-template:TPL-TEST-01",
                  "templateId": "TPL-TEST-01",
                  "intent": "TestIntent",
                  "tone": "ProfessionalWarm",
                  "subjectPattern": "Subject {{X}}",
                  "bodyGuidance": ["Guidance A"],
                  "language": "vi",
                  "synthetic": true,
                  "version": "1.0"
                }]
                """);

            var documents = KnowledgeSourceLoader.LoadFromDirectory(directory);

            var product = Assert.Single(documents, d => d.DocumentType == KnowledgeDocumentType.Product);
            Assert.Equal("kb:product:PRD-TEST-01", product.SourceId);
            Assert.Equal("PRD-TEST-01", product.ProductCode);
            Assert.Null(product.TemplateId);
            Assert.Equal("vi", product.Language);
            Assert.Equal("1.0", product.Version);
            Assert.Contains("Test Product", product.RenderedText, StringComparison.Ordinal);
            Assert.Contains("Summary text", product.RenderedText, StringComparison.Ordinal);
            Assert.Contains("Eligibility A", product.RenderedText, StringComparison.Ordinal);
            Assert.Contains("Benefit A", product.RenderedText, StringComparison.Ordinal);
            Assert.Contains("Constraint A", product.RenderedText, StringComparison.Ordinal);

            var template = Assert.Single(documents, d => d.DocumentType == KnowledgeDocumentType.EmailTemplate);
            Assert.Equal("kb:email-template:TPL-TEST-01", template.SourceId);
            Assert.Equal("TPL-TEST-01", template.TemplateId);
            Assert.Null(template.ProductCode);
            Assert.Contains("TestIntent", template.RenderedText, StringComparison.Ordinal);
            Assert.Contains("Subject {{X}}", template.RenderedText, StringComparison.Ordinal);
            Assert.Contains("Guidance A", template.RenderedText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Validate_DuplicateSourceIdAcrossBothFiles_Throws()
    {
        var product = new ProductKnowledgeDto(
            "kb:product:PRD-DUP-01", "PRD-DUP-01", "Name", "Savings", "Summary",
            ["E"], ["B"], ["C"], "vi", true, "1.0");

        var duplicateAsTemplate = new EmailTemplateKnowledgeDto(
            "kb:product:PRD-DUP-01", "TPL-DUP-01", "Intent", "ProfessionalWarm", "Subject",
            ["G"], "vi", true, "1.0");

        var exception = Assert.Throws<InvalidOperationException>(
            () => KnowledgeSourceLoader.Validate([product], [duplicateAsTemplate]));
        Assert.Contains("Duplicate sourceId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WrongProductPrefix_Throws()
    {
        var product = new ProductKnowledgeDto(
            "wrong:prefix:PRD-01", "PRD-01", "Name", "Savings", "Summary",
            ["E"], ["B"], ["C"], "vi", true, "1.0");

        var exception = Assert.Throws<InvalidOperationException>(() => KnowledgeSourceLoader.Validate([product], []));
        Assert.Contains("kb:product:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WrongTemplatePrefix_Throws()
    {
        var template = new EmailTemplateKnowledgeDto(
            "wrong:prefix:TPL-01", "TPL-01", "Intent", "ProfessionalWarm", "Subject",
            ["G"], "vi", true, "1.0");

        var exception = Assert.Throws<InvalidOperationException>(() => KnowledgeSourceLoader.Validate([], [template]));
        Assert.Contains("kb:email-template:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NotSynthetic_Throws()
    {
        var product = new ProductKnowledgeDto(
            "kb:product:PRD-01", "PRD-01", "Name", "Savings", "Summary",
            ["E"], ["B"], ["C"], "vi", Synthetic: false, "1.0");

        var exception = Assert.Throws<InvalidOperationException>(() => KnowledgeSourceLoader.Validate([product], []));
        Assert.Contains("synthetic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WrongLanguage_Throws()
    {
        var product = new ProductKnowledgeDto(
            "kb:product:PRD-01", "PRD-01", "Name", "Savings", "Summary",
            ["E"], ["B"], ["C"], "en", true, "1.0");

        var exception = Assert.Throws<InvalidOperationException>(() => KnowledgeSourceLoader.Validate([product], []));
        Assert.Contains("language", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_MultipleErrors_AreAllReported()
    {
        var product = new ProductKnowledgeDto(
            "wrong:prefix", "", "Name", "Savings", "Summary",
            ["E"], ["B"], ["C"], "en", Synthetic: false, "1.0");

        var exception = Assert.Throws<InvalidOperationException>(() => KnowledgeSourceLoader.Validate([product], []));

        Assert.Contains("prefix", exception.Message, StringComparison.Ordinal);
        Assert.Contains("synthetic", exception.Message, StringComparison.Ordinal);
        Assert.Contains("language", exception.Message, StringComparison.Ordinal);
        Assert.Contains("productCode", exception.Message, StringComparison.Ordinal);
    }
}
