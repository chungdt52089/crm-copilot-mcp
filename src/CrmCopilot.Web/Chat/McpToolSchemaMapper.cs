using Google.GenAI.Types;
using ModelContextProtocol.Client;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Maps a discovered MCP tool straight into a Gemini FunctionDeclaration (plan D2).
/// McpClientTool.JsonSchema is standard JSON Schema; FunctionDeclaration.ParametersJsonSchema
/// accepts a raw JSON Schema object directly (confirmed via the installed Google.GenAI 1.19.0
/// XML docs — mutually exclusive with the typed Parameters/Schema object graph) — no manual
/// JSON-Schema-to-OpenAPI-Schema translation needed.
/// </summary>
internal static class McpToolSchemaMapper
{
    public static FunctionDeclaration ToFunctionDeclaration(McpClientTool tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        ParametersJsonSchema = tool.JsonSchema,
    };
}
