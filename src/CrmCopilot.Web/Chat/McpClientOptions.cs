namespace CrmCopilot.Web.Chat;

public sealed class McpClientOptions
{
    /// <summary>Flat config key, matching .env.example's MCPSERVER_BASE_URL exactly.</summary>
    public const string BaseUrlConfigKey = "MCPSERVER_BASE_URL";

    public string BaseUrl { get; set; } = string.Empty;
}
