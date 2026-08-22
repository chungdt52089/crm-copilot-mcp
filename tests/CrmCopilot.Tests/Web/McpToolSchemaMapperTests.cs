using System.Text.Json;
using CrmCopilot.Tests.TestSupport;
using CrmCopilot.Web.Chat;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// D2: a sample MCP tool JSON schema round-trips into FunctionDeclaration.ParametersJsonSchema
/// unchanged — no manual JSON-Schema-to-OpenAPI-Schema translation.
/// </summary>
public class McpToolSchemaMapperTests
{
    [Fact]
    public async Task ToFunctionDeclaration_MapsNameDescriptionAndRawJsonSchema()
    {
        await using var factory = McpServerTestHost.CreateWithMockCrmApiBaseUrl(McpServerTestHost.ValidMockCrmApiBaseUrl);
        var httpClient = factory.CreateClient();
        var transport = new ModelContextProtocol.Client.HttpClientTransport(
            new ModelContextProtocol.Client.HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "mcp"),
                TransportMode = ModelContextProtocol.Client.HttpTransportMode.StreamableHttp,
            },
            httpClient, ownsHttpClient: true);
        await using var client = await ModelContextProtocol.Client.McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var getCustomerTool = Assert.Single(tools, t => t.Name == "get_customer");

        var declaration = McpToolSchemaMapper.ToFunctionDeclaration(getCustomerTool);

        Assert.Equal("get_customer", declaration.Name);
        Assert.Equal(getCustomerTool.Description, declaration.Description);
        Assert.NotNull(declaration.ParametersJsonSchema);
        Assert.Equal(
            JsonSerializer.Serialize(getCustomerTool.JsonSchema),
            JsonSerializer.Serialize(declaration.ParametersJsonSchema));
    }
}
