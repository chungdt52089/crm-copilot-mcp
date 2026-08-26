namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// Registers the generate_call_script collaborators. Reuses the Google.GenAI Client singleton that
/// AddKnowledgeRetrieval already registers off GEMINI_API_KEY — it does not register a second
/// Client and adds no new ValidateOnStart options (no new secret to bind).
///
/// This wires DI only. Publishing the tool to tools/list is a separate, explicit
/// .WithTools&lt;CallScriptTools&gt;() call on the MCP server builder (plan D16) — calling this
/// method alone would leave the tool invisible to every client.
/// </summary>
public static class CallScriptServiceCollectionExtensions
{
    public static IServiceCollection AddCallScriptGeneration(this IServiceCollection services)
    {
        services.AddSingleton<ICallScriptGenerator, GeminiCallScriptGenerator>();
        services.AddSingleton<ICallScriptTemplateCatalog, CallScriptTemplateCatalog>();
        return services;
    }
}
