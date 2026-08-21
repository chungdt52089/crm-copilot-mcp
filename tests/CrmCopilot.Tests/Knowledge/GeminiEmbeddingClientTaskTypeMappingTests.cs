using CrmCopilot.McpServer.Knowledge;

namespace CrmCopilot.Tests.Knowledge;

public class GeminiEmbeddingClientTaskTypeMappingTests
{
    [Fact]
    public void MapTaskType_Document_ReturnsRetrievalDocument()
    {
        Assert.Equal("RETRIEVAL_DOCUMENT", GeminiEmbeddingClient.MapTaskType(EmbeddingTaskType.Document));
    }

    [Fact]
    public void MapTaskType_Query_ReturnsRetrievalQuery()
    {
        Assert.Equal("RETRIEVAL_QUERY", GeminiEmbeddingClient.MapTaskType(EmbeddingTaskType.Query));
    }
}
