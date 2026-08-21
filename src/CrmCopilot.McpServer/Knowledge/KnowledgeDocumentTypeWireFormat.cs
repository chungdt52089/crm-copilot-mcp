using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;

namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// The single place that owns the "product"/"email_template" string literals used in Chroma
/// metadata (docs/08_RAG_EMAIL_AND_PII_SPEC.md §2) — shared by the write side (ChromaHttpClient
/// upsert) and the read side (ChromaMetadataParser) so they cannot drift apart.
/// </summary>
internal static class KnowledgeDocumentTypeWireFormat
{
    public const string Product = "product";
    public const string EmailTemplate = "email_template";

    public static string ToWire(KnowledgeDocumentType documentType) => documentType switch
    {
        KnowledgeDocumentType.Product => Product,
        KnowledgeDocumentType.EmailTemplate => EmailTemplate,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null),
    };

    public static KnowledgeDocumentType FromWire(string sourceId, string wireValue) => wireValue switch
    {
        Product => KnowledgeDocumentType.Product,
        EmailTemplate => KnowledgeDocumentType.EmailTemplate,
        _ => throw new KnowledgeVectorStoreException(
            $"Chroma metadata for '{sourceId}' has an unrecognized documentType '{wireValue}'.", retryable: false),
    };

    /// <summary>
    /// Non-throwing counterpart to <see cref="FromWire"/>, for validating untrusted P0-04 tool
    /// input (search_product_knowledge's documentTypes filter) — an unrecognized value there is a
    /// client INVALID_ARGUMENT, not the Chroma-metadata-corruption failure FromWire represents.
    /// </summary>
    public static bool TryFromWire(string wireValue, out KnowledgeDocumentType documentType)
    {
        switch (wireValue)
        {
            case Product:
                documentType = KnowledgeDocumentType.Product;
                return true;
            case EmailTemplate:
                documentType = KnowledgeDocumentType.EmailTemplate;
                return true;
            default:
                documentType = default;
                return false;
        }
    }
}
