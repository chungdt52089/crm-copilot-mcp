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

    // P0-10 — the three tools that complete the seven-tool set (docs/07 §2/§10).
    public const string GetOpportunities = "get_opportunities";
    public const string GetCampaigns = "get_campaigns";
    public const string GenerateCallScript = "generate_call_script";

    // P0-14 (PD-021) — the eighth tool, and the only write one. Present for EVERY role: the Host
    // allowlist is not an authorization boundary and never varies by user. Hiding it from an RM here
    // would mean the call never happens and the MCP server has no refusal to log, which is the
    // opposite of what PD-022 is for.
    public const string DeleteCustomer = "delete_customer";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [GetCustomer, GetInteractions, SearchProductKnowledge, GenerateEmail, GetOpportunities, GetCampaigns, GenerateCallScript, DeleteCustomer],
        StringComparer.Ordinal);
}
