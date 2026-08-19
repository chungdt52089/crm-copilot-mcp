namespace CrmCopilot.McpServer.Crm;

public sealed class MockCrmGatewayOptions
{
    /// <summary>Flat config key, matching .env.example's MOCKCRM_API_BASE_URL exactly so the
    /// standard environment-variable provider binds it without nested "__" syntax.</summary>
    public const string ConfigKey = "MOCKCRM_API_BASE_URL";

    public string BaseUrl { get; set; } = string.Empty;
}
