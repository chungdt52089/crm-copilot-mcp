namespace CrmCopilot.Web.Chat;

/// <summary>
/// The P0-05-approved MCP tool set (plan D5). Gemini is only ever shown the intersection of this
/// set and whatever CrmCopilot.McpServer's tools/list actually returns — a tool the server exposes
/// but that isn't in this set is structurally invisible to Gemini, not filtered after the fact.
/// </summary>
internal static class ApprovedMcpToolNames
{
    public const string GetCustomer = "get_customer";
    public const string GetInteractions = "get_interactions";
    public const string SearchProductKnowledge = "search_product_knowledge";
    public const string GenerateEmail = "generate_email";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>([GetCustomer, GetInteractions, SearchProductKnowledge, GenerateEmail], StringComparer.Ordinal);
}
