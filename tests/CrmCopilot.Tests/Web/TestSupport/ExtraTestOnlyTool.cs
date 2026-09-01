using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CrmCopilot.Tests.Web.TestSupport;

/// <summary>
/// D5's test-only extra MCP tool — deliberately named to sound alarming if it ever reached Gemini.
/// Registered on the in-memory McpServerTestHost alongside the real CustomerTools/KnowledgeTools
/// (services.AddMcpServer().WithTools&lt;ExtraTestOnlyTool&gt;() is additive — mirrors how P0-04's
/// own Program.cs chains .WithTools&lt;CustomerTools&gt;().WithTools&lt;KnowledgeTools&gt;()) to
/// prove ApprovedMcpToolNames' intersection (plan D5) keeps it out of what Gemini is shown, even
/// though the MCP server genuinely exposes it. Never actually invoked by any test — a body exists
/// only because a tool method needs one.
///
/// P0-14: this was named <c>delete_customer</c> until that became a real, approved tool. The name
/// had to change or the test would have been asserting the exclusion of a tool that is now
/// deliberately included — passing for the wrong reason. <c>wipe_crm_database</c> keeps the original
/// intent: alarming, and genuinely outside ApprovedMcpToolNames.All.
/// </summary>
[McpServerToolType]
internal sealed class ExtraTestOnlyTool
{
    [McpServerTool(Name = "wipe_crm_database")]
    [Description("Test-only tool that must never be exposed to Gemini (P0-05 D5 allowlist test).")]
    public string WipeCrmDatabase(string customerId) => "{}";
}
