using System.Text.Json;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.McpServer.Knowledge;

namespace CrmCopilot.Tests.Knowledge;

/// <summary>
/// Parses genuine System.Text.Json-deserialized Dictionary&lt;string, JsonElement&gt; fixtures
/// (not hand-built dictionaries) — the exact shape ChromaHttpClient actually receives from a
/// real HTTP response body, so this proves the fingerprint/dimension/normalized round-trip
/// comparisons are type-correct, not just structurally similar-looking.
/// </summary>
public class ChromaMetadataParserTests
{
    private static Dictionary<string, JsonElement> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private const string FullProductJson = """
        {
          "sourceId": "kb:product:PRD-SAV-006M",
          "documentType": "product",
          "productCode": "PRD-SAV-006M",
          "language": "vi",
          "version": "1.0",
          "embeddingModel": "gemini-embedding-001",
          "embeddingDimension": 768,
          "distanceMetric": "l2",
          "normalized": true,
          "fingerprint": "abc123fingerprint"
        }
        """;

    [Fact]
    public void Parse_FullProductMetadata_RoundTripsAllFieldsWithCorrectTypes()
    {
        var metadata = ChromaMetadataParser.Parse("kb:product:PRD-SAV-006M", Deserialize(FullProductJson));

        Assert.Equal("kb:product:PRD-SAV-006M", metadata.SourceId);
        Assert.Equal(KnowledgeDocumentType.Product, metadata.DocumentType);
        Assert.Equal("PRD-SAV-006M", metadata.ProductCode);
        Assert.Null(metadata.TemplateId);
        Assert.Equal("vi", metadata.Language);
        Assert.Equal("1.0", metadata.Version);
        Assert.Equal("gemini-embedding-001", metadata.EmbeddingModel);

        // The literal proof this test exists for: comparing the parsed value against a real int/
        // bool/string by value and type — not a boxed JsonElement compared against one, which
        // would silently fail with a naive Dictionary<string, object> deserialization instead.
        Assert.Equal(768, metadata.EmbeddingDimension);
        Assert.IsType<int>(metadata.EmbeddingDimension);
        Assert.True(metadata.Normalized);
        Assert.IsType<bool>(metadata.Normalized);
        Assert.Equal("abc123fingerprint", metadata.Fingerprint);
        Assert.IsType<string>(metadata.Fingerprint);

        // The idempotency skip-check this all exists to support:
        Assert.True(metadata.Fingerprint == "abc123fingerprint");
    }

    [Fact]
    public void Parse_EmailTemplateMetadata_HasTemplateIdNotProductCode()
    {
        const string json = """
            {
              "sourceId": "kb:email-template:TPL-EMAIL-MATURITY-01",
              "documentType": "email_template",
              "templateId": "TPL-EMAIL-MATURITY-01",
              "language": "vi",
              "version": "1.0",
              "embeddingModel": "gemini-embedding-001",
              "embeddingDimension": 768,
              "distanceMetric": "l2",
              "normalized": true,
              "fingerprint": "def456fingerprint"
            }
            """;

        var metadata = ChromaMetadataParser.Parse("kb:email-template:TPL-EMAIL-MATURITY-01", Deserialize(json));

        Assert.Equal(KnowledgeDocumentType.EmailTemplate, metadata.DocumentType);
        Assert.Equal("TPL-EMAIL-MATURITY-01", metadata.TemplateId);
        Assert.Null(metadata.ProductCode);
    }

    [Fact]
    public void Parse_MissingRequiredField_ThrowsNamingFieldAndSourceId()
    {
        const string json = """{ "documentType": "product", "language": "vi", "version": "1.0", "embeddingModel": "m", "embeddingDimension": 768, "distanceMetric": "l2", "normalized": true, "fingerprint": "f" }""";

        var exception = Assert.Throws<KnowledgeVectorStoreException>(
            () => ChromaMetadataParser.Parse("kb:product:PRD-01", Deserialize(json)));

        Assert.Contains("kb:product:PRD-01", exception.Message, StringComparison.Ordinal);
        Assert.Contains("sourceId", exception.Message, StringComparison.Ordinal);
        Assert.False(exception.Retryable);
    }

    [Fact]
    public void Parse_WrongTypeForEmbeddingDimension_Throws()
    {
        const string json = """
            {
              "sourceId": "kb:product:PRD-01", "documentType": "product", "productCode": "PRD-01",
              "language": "vi", "version": "1.0", "embeddingModel": "m",
              "embeddingDimension": "not-a-number", "distanceMetric": "l2", "normalized": true, "fingerprint": "f"
            }
            """;

        var exception = Assert.Throws<KnowledgeVectorStoreException>(
            () => ChromaMetadataParser.Parse("kb:product:PRD-01", Deserialize(json)));

        Assert.Contains("embeddingDimension", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnrecognizedDocumentType_Throws()
    {
        const string json = """
            {
              "sourceId": "kb:product:PRD-01", "documentType": "unknown_type", "productCode": "PRD-01",
              "language": "vi", "version": "1.0", "embeddingModel": "m",
              "embeddingDimension": 768, "distanceMetric": "l2", "normalized": true, "fingerprint": "f"
            }
            """;

        var exception = Assert.Throws<KnowledgeVectorStoreException>(
            () => ChromaMetadataParser.Parse("kb:product:PRD-01", Deserialize(json)));

        Assert.Contains("documentType", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToWireMetadata_OmitsNullProductCodeAndTemplateId()
    {
        var metadata = ChromaMetadataParser.Parse("kb:product:PRD-SAV-006M", Deserialize(FullProductJson));

        var wire = ChromaMetadataParser.ToWireMetadata(metadata);

        Assert.True(wire.ContainsKey("productCode"));
        Assert.False(wire.ContainsKey("templateId"));
    }
}
