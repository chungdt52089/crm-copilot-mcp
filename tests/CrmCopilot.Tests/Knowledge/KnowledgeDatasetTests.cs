using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;

namespace CrmCopilot.Tests.Knowledge;

/// <summary>
/// Golden checks against the checked-in data/knowledge/*.json files (docs/06_DATA_AND_MOCK_API_SPEC.md
/// §2/§3, docs/03_ACCEPTANCE_CRITERIA.md §7) — mirrors CrmCopilot.Tests.Crm.DatasetValidationTests.
/// </summary>
public class KnowledgeDatasetTests
{
    private static IReadOnlyList<KnowledgeSourceDocument> LoadDocuments() => KnowledgeSourceLoader.LoadFromAppBaseDirectory();

    [Fact]
    public void Dataset_LoadsExactly14Documents()
    {
        var documents = LoadDocuments();

        Assert.Equal(14, documents.Count);
        Assert.Equal(6, documents.Count(d => d.DocumentType == KnowledgeDocumentType.Product));
        Assert.Equal(8, documents.Count(d => d.DocumentType == KnowledgeDocumentType.EmailTemplate));
    }

    [Fact]
    public void CanonicalProduct_IsPresent()
    {
        var documents = LoadDocuments();

        Assert.Contains(documents, d => d.SourceId == "kb:product:PRD-SAV-006M" && d.ProductCode == "PRD-SAV-006M");
    }

    [Fact]
    public void CanonicalEmailTemplate_IsPresent()
    {
        var documents = LoadDocuments();

        Assert.Contains(documents, d => d.SourceId == "kb:email-template:TPL-EMAIL-MATURITY-01" && d.TemplateId == "TPL-EMAIL-MATURITY-01");
    }

    [Fact]
    public void AllSourceIds_AreUnique()
    {
        var ids = LoadDocuments().Select(d => d.SourceId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AllDocuments_UseVietnameseLanguage()
    {
        Assert.All(LoadDocuments(), d => Assert.Equal("vi", d.Language));
    }

    [Fact]
    public void AllDocuments_HaveNonEmptyRenderedText()
    {
        Assert.All(LoadDocuments(), d => Assert.False(string.IsNullOrWhiteSpace(d.RenderedText)));
    }

    [Fact]
    public void Products_HaveProductCodeAndNoTemplateId()
    {
        var products = LoadDocuments().Where(d => d.DocumentType == KnowledgeDocumentType.Product);

        Assert.All(products, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.ProductCode));
            Assert.Null(d.TemplateId);
        });
    }

    [Fact]
    public void EmailTemplates_HaveTemplateIdAndNoProductCode()
    {
        var templates = LoadDocuments().Where(d => d.DocumentType == KnowledgeDocumentType.EmailTemplate);

        Assert.All(templates, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.TemplateId));
            Assert.Null(d.ProductCode);
        });
    }
}
