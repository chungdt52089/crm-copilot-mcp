using CrmCopilot.Contracts.Auth;

namespace CrmCopilot.McpServer.Auth;

/// <summary>
/// P0-13 role → permitted tools (docs/15 §4, PD-022/PD-024). This is the ONLY authorization table
/// in the project, and it is consulted at the MCP boundary — never in the Host.
///
/// Note what is deliberately absent: nothing here filters <c>tools/list</c>. Discovery stays open
/// for every role, so an RM's model can still request a tool it is not allowed to use — which is
/// precisely what makes the refusal (and its DENIED audit line) happen at all. Hiding a tool would
/// mean the call never occurs and there is nothing to log.
/// </summary>
internal static class ToolPolicy
{
    private static readonly IReadOnlySet<string> RmTools = new HashSet<string>(
        [
            "get_customer",
            "get_interactions",
            "search_product_knowledge",
            "generate_email",
            "get_opportunities",
            "get_campaigns",
            "generate_call_script",
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> AuditorTools = new HashSet<string>(
        ["get_customer", "get_interactions"],
        StringComparer.Ordinal);

    public const string DeniedReason = "role_not_permitted";

    /// <summary>
    /// Fails closed: an absent, blank, or unrecognized role is denied every tool. Admin is allowed
    /// everything, including tools added later (P0-14's delete_customer), by design.
    /// </summary>
    public static bool IsAllowed(string? role, string toolName) => role switch
    {
        Roles.Admin => true,
        Roles.RM => RmTools.Contains(toolName),
        Roles.Auditor => AuditorTools.Contains(toolName),
        _ => false,
    };
}
