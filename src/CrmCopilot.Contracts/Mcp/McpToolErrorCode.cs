namespace CrmCopilot.Contracts.Mcp;

/// <summary>
/// <see cref="McpToolError.Code"/> values (docs/07_MCP_TOOL_CONTRACTS.md §3 defines a larger enum
/// shared across all four P0 tools). <c>AMBIGUOUS_MATCH</c> is intentionally not a member — an
/// ambiguous customer match is carried via <see cref="McpToolStatus.Ambiguous"/> with
/// <see cref="McpToolResult.Error"/> left null (see McpToolResult remarks), not as an error code.
/// <c>MODEL_ERROR</c> is <c>generate_email</c> (P0-07) territory only — never produced by
/// get_customer/get_interactions/search_product_knowledge.
/// </summary>
public static class McpToolErrorCode
{
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string NotFound = "NOT_FOUND";
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";
    public const string RagUnavailable = "RAG_UNAVAILABLE";
    public const string ModelError = "MODEL_ERROR";
    public const string InternalError = "INTERNAL_ERROR";
}
