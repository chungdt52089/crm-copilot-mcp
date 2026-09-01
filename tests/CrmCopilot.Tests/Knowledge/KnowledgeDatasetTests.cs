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

    /// <summary>
    /// P0-10 raised this from 14 to 21 by adding 7 call-script playbooks. The per-type counts matter
    /// as much as the total: the ingestion run this checkpoint reports (7 embedded / 14 unchanged /
    /// collection count 21) is only meaningful if the 14 pre-existing documents are still exactly
    /// the same 6 products and 8 email templates.
    /// </summary>
    [Fact]
    public void Dataset_LoadsExactly21Documents()
    {
        var documents = LoadDocuments();

        Assert.Equal(21, documents.Count);
        Assert.Equal(6, documents.Count(d => d.DocumentType == KnowledgeDocumentType.Product));
        Assert.Equal(8, documents.Count(d => d.DocumentType == KnowledgeDocumentType.EmailTemplate));
        Assert.Equal(7, documents.Count(d => d.DocumentType == KnowledgeDocumentType.CallScript));
    }

    /// <summary>
    /// Every source id in the corpus is unique. LiveRagAcceptanceTests derives its expected vector
    /// count from documents.Count rather than a hard-coded literal, which is only sound while this
    /// holds — a duplicated id would inflate the count and make the live idempotency assertions
    /// agree with themselves. Asserted offline so the property is guarded without live credentials.
    /// </summary>
    [Fact]
    public void Dataset_SourceIds_AreAllUnique()
    {
        var sourceIds = LoadDocuments().Select(d => d.SourceId).ToList();

        Assert.Equal(sourceIds.Count, sourceIds.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The playbook the periodic-care fallback pins by id (plan Amendment A6 step 4). If it
    /// ever leaves the dataset the short-sentence demo silently stops being deterministic, so its
    /// presence is asserted rather than assumed.</summary>
    [Fact]
    public void PinnedPeriodicCareCallScript_IsPresent()
    {
        var documents = LoadDocuments();

        Assert.Contains(
            documents,
            d => d.SourceId == "kb:call-script:CS-CALL-PERIODIC-CARE-01" && d.TemplateId == "CS-CALL-PERIODIC-CARE-01");
    }

    [Fact]
    public void CanonicalSavingsCallScript_IsPresent()
    {
        var documents = LoadDocuments();

        Assert.Contains(
            documents,
            d => d.SourceId == "kb:call-script:CS-CALL-SAVINGS-FOLLOWUP-01" && d.DocumentType == KnowledgeDocumentType.CallScript);
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
