namespace CrmCopilot.Contracts.Auth;

/// <summary>
/// P0-12/P0-13 demo roles (docs/01 PD-024). Shared by CrmCopilot.Web (which mints the role claim
/// from data/auth/users.json) and CrmCopilot.McpServer (whose ToolPolicy keys off it), so the two
/// sides cannot drift on spelling.
/// </summary>
public static class Roles
{
    public const string RM = "RM";
    public const string Auditor = "Auditor";
    public const string Admin = "Admin";
}
