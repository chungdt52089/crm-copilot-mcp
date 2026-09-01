namespace CrmCopilot.McpServer.Email;

/// <summary>
/// Registers IEmailDraftGenerator -&gt; GeminiEmailDraftGenerator for generate_email. Reuses the
/// Client singleton KnowledgeServiceCollectionExtensions.AddKnowledgeRetrieval already registers
/// off GEMINI_API_KEY — does not register a second Client instance, and adds no new
/// ValidateOnStart options (no new secret to bind).
/// </summary>
public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEmailGeneration(this IServiceCollection services)
    {
        services.AddSingleton<IEmailDraftGenerator, GeminiEmailDraftGenerator>();
        return services;
    }
}
