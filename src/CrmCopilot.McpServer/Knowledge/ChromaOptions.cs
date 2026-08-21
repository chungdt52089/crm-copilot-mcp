namespace CrmCopilot.McpServer.Knowledge;

public sealed class ChromaOptions
{
    /// <summary>Flat config key, matching .env.example's CHROMA_BASE_URL exactly.</summary>
    public const string BaseUrlConfigKey = "CHROMA_BASE_URL";

    /// <summary>Optional, non-required override — has a safe default, so it is not part of
    /// ValidateOnStart and deliberately not listed in .env.example (that file documents
    /// checkpoint-required variables). Lets the opt-in live acceptance run target an isolated
    /// collection without a code change.</summary>
    public const string CollectionNameConfigKey = "CHROMA_COLLECTION_NAME";

    public const string DefaultCollectionName = "crm-copilot-knowledge";
    public const string DistanceMetric = "l2";
    public const string Tenant = "default_tenant";
    public const string Database = "default_database";

    public string BaseUrl { get; set; } = string.Empty;
    public string CollectionName { get; set; } = DefaultCollectionName;
}
