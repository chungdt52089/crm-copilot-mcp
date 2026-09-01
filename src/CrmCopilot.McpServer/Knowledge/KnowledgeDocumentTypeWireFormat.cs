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

    /// <summary>P0-10. Note the forward-only compatibility consequence: a Chroma collection that
    /// has been ingested with call-script documents cannot be read by a pre-P0-10 build of this
    /// server — <see cref="FromWire"/> there would reject the value. Rolling the server back
    /// requires re-ingesting the collection.</summary>
    public const string CallScript = "call_script";

    public static string ToWire(KnowledgeDocumentType documentType) => documentType switch
    {
        KnowledgeDocumentType.Product => Product,
        KnowledgeDocumentType.EmailTemplate => EmailTemplate,
        KnowledgeDocumentType.CallScript => CallScript,
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, null),
    };

    public static KnowledgeDocumentType FromWire(string sourceId, string wireValue) => wireValue switch
    {
        Product => KnowledgeDocumentType.Product,
        EmailTemplate => KnowledgeDocumentType.EmailTemplate,
        CallScript => KnowledgeDocumentType.CallScript,
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
            case CallScript:
                documentType = KnowledgeDocumentType.CallScript;
                return true;
            default:
                documentType = default;
                return false;
        }
    }
}
