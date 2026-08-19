using CrmCopilot.Contracts.Crm;
using Microsoft.Extensions.Options;

namespace CrmCopilot.McpServer.Crm;

public static class CrmGatewayServiceCollectionExtensions
{
    /// <summary>
    /// Registers ICrmGateway -&gt; MockCrmGateway with typed, fail-fast-on-start options bound
    /// to MOCKCRM_API_BASE_URL. Not called by any tool/endpoint yet (P0-04 wires a consumer).
    /// </summary>
    public static IServiceCollection AddCrmGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MockCrmGatewayOptions>()
            .Configure(options => options.BaseUrl = configuration[MockCrmGatewayOptions.ConfigKey] ?? string.Empty)
            .Validate(
                options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                $"{MockCrmGatewayOptions.ConfigKey} must be a non-empty absolute URL.")
            .ValidateOnStart();

        services.AddHttpClient<ICrmGateway, MockCrmGateway>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MockCrmGatewayOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });

        return services;
    }
}
