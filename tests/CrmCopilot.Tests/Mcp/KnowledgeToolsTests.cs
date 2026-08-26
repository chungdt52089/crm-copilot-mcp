using System.Text.Json;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.Tests.Knowledge.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.Mcp;

/// <summary>
/// Method-level coverage of every branch in the P0-04 plan's KnowledgeTools mapping table — fast,
/// offline, against FakeKnowledgeRetriever. Protocol-level (real MCP tools/call) coverage of a
/// subset of these lives in McpToolProtocolTests.cs.
/// </summary>
public class KnowledgeToolsTests
{
    private static readonly KnowledgeSourceMetadata SampleMetadata = new(
        "kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "PRD-SAV-006M", null,
        "vi", "1.0", "gemini-embedding-001", 768, "l2", true, "fingerprint");

    private static KnowledgeTools CreateTools(FakeKnowledgeRetriever retriever) =>
        new(retriever, new HttpContextAccessor(), NullLogger<KnowledgeTools>.Instance);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task SearchProductKnowledge_BlankQuery_ReturnsInvalidArgument()
    {
        var tools = CreateTools(new FakeKnowledgeRetriever());

        var result = await tools.SearchProductKnowledge("   ", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SearchProductKnowledge_QueryTooLong_ReturnsInvalidArgumentWithoutCallingRetriever()
    {
        var retriever = new FakeKnowledgeRetriever();
        var tools = CreateTools(retriever);
        var tooLong = new string('a', 1001);

        var result = await tools.SearchProductKnowledge(tooLong, cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(retriever.LastQuery);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SearchProductKnowledge_TopKOutOfRange_ReturnsInvalidArgument(int topK)
    {
        var tools = CreateTools(new FakeKnowledgeRetriever());

        var result = await tools.SearchProductKnowledge("query", topK, cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SearchProductKnowledge_UnrecognizedDocumentType_ReturnsInvalidArgument()
    {
        var tools = CreateTools(new FakeKnowledgeRetriever());

        var result = await tools.SearchProductKnowledge(
            "query", documentTypes: ["not_a_real_type"], cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SearchProductKnowledge_EmbeddingException_ReturnsRagUnavailable()
    {
        var retriever = new FakeKnowledgeRetriever { ThrowOnSearch = new KnowledgeEmbeddingException("boom", retryable: true) };
        var tools = CreateTools(retriever);

        var result = await tools.SearchProductKnowledge("query", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.RagUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("boom", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchProductKnowledge_VectorStoreException_ReturnsRagUnavailable()
    {
        var retriever = new FakeKnowledgeRetriever { ThrowOnSearch = new KnowledgeVectorStoreException("boom", retryable: false) };
        var tools = CreateTools(retriever);

        var result = await tools.SearchProductKnowledge("query", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolErrorCode.RagUnavailable, root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(root.GetProperty("error").GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task SearchProductKnowledge_NoRelevantEvidence_ReturnsNotFoundWithNullError()
    {
        var retriever = new FakeKnowledgeRetriever { SearchResult = KnowledgeSearchResult.NoRelevantEvidence };
        var tools = CreateTools(retriever);

        var result = await tools.SearchProductKnowledge("query", cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.NotFound, root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task SearchProductKnowledge_Found_ReturnsSuccessWithDistanceNoTitleNoScore()
    {
        var match = new KnowledgeMatch("kb:product:PRD-SAV-006M", KnowledgeDocumentType.Product, "nội dung sản phẩm", SampleMetadata, Distance: 0.47);
        var retriever = new FakeKnowledgeRetriever { SearchResult = KnowledgeSearchResult.Found([match]) };
        var tools = CreateTools(retriever);

        var result = await tools.SearchProductKnowledge("Khách hàng quan tâm gửi tiết kiệm an toàn kỳ hạn 6 tháng", topK: 3, cancellationToken: TestContext.Current.CancellationToken);

        var root = Parse(result);
        Assert.Equal(McpToolStatus.Success, root.GetProperty("status").GetString());
        var firstMatch = root.GetProperty("data").GetProperty("matches")[0];
        Assert.Equal("kb:product:PRD-SAV-006M", firstMatch.GetProperty("sourceId").GetString());
        Assert.Equal("product", firstMatch.GetProperty("documentType").GetString());
        Assert.Equal(0.47, firstMatch.GetProperty("distance").GetDouble());
        Assert.False(firstMatch.TryGetProperty("title", out _));
        Assert.False(firstMatch.TryGetProperty("score", out _));
        Assert.Equal("kb:product:PRD-SAV-006M", root.GetProperty("sourceIds")[0].GetString());
    }

    [Fact]
    public async Task SearchProductKnowledge_DocumentTypesProvided_MapsToEnumBeforeCallingRetriever()
    {
        var retriever = new FakeKnowledgeRetriever { SearchResult = KnowledgeSearchResult.NoRelevantEvidence };
        var tools = CreateTools(retriever);

        await tools.SearchProductKnowledge("query", documentTypes: ["product", "email_template"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(retriever.LastQuery);
        Assert.Equal([KnowledgeDocumentType.Product, KnowledgeDocumentType.EmailTemplate], retriever.LastQuery!.DocumentTypes);
    }

    /// <summary>
    /// Plan D7, the regression P0-10 introduced the risk of. Before call scripts shared the Chroma
    /// collection, omitting documentTypes meant "no filter", which happened to equal
    /// [Product, EmailTemplate]. It no longer does. The default is now stated explicitly, so an
    /// unfiltered search cannot start returning call scripts and disturbing this tool's verified
    /// results — including the canonical PRD-SAV-006M ranking the acceptance evidence rests on.
    /// </summary>
    [Fact]
    public async Task SearchProductKnowledge_DocumentTypesOmitted_FiltersToProductAndEmailTemplateExplicitly()
    {
        var retriever = new FakeKnowledgeRetriever { SearchResult = KnowledgeSearchResult.NoRelevantEvidence };
        var tools = CreateTools(retriever);

        await tools.SearchProductKnowledge("query", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(retriever.LastQuery);
        Assert.NotNull(retriever.LastQuery!.DocumentTypes);
        Assert.Equal([KnowledgeDocumentType.Product, KnowledgeDocumentType.EmailTemplate], retriever.LastQuery.DocumentTypes);
        Assert.DoesNotContain(KnowledgeDocumentType.CallScript, retriever.LastQuery.DocumentTypes!);
    }

    [Fact]
    public async Task SearchProductKnowledge_EmptyDocumentTypesList_AlsoFiltersExplicitly()
    {
        var retriever = new FakeKnowledgeRetriever { SearchResult = KnowledgeSearchResult.NoRelevantEvidence };
        var tools = CreateTools(retriever);

        await tools.SearchProductKnowledge("query", documentTypes: [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([KnowledgeDocumentType.Product, KnowledgeDocumentType.EmailTemplate], retriever.LastQuery!.DocumentTypes);
    }

    /// <summary>
    /// call_script is a real KnowledgeDocumentType, but it is outside this tool's contract
    /// (docs/07 §6). Requesting it explicitly is an INVALID_ARGUMENT, not a widening of the filter —
    /// call scripts are reachable only through generate_call_script's own retrieval.
    /// </summary>
    [Fact]
    public async Task SearchProductKnowledge_CallScriptDocumentType_IsRejected()
    {
        var retriever = new FakeKnowledgeRetriever { SearchResult = KnowledgeSearchResult.NoRelevantEvidence };
        var tools = CreateTools(retriever);

        var result = await tools.SearchProductKnowledge(
            "query", documentTypes: ["call_script"], cancellationToken: TestContext.Current.CancellationToken);

        var root = JsonDocument.Parse(result).RootElement;
        Assert.Equal(McpToolStatus.Error, root.GetProperty("status").GetString());
        Assert.Equal(McpToolErrorCode.InvalidArgument, root.GetProperty("error").GetProperty("code").GetString());
        Assert.Null(retriever.LastQuery);
    }
}
