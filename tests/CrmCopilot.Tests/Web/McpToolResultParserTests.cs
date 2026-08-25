using System.Text.Json;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Web.Chat;
using ModelContextProtocol.Protocol;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// D9(d) (malformed MCP envelope — only feasible as a unit test, since the three approved MCP
/// tools always produce well-formed envelopes by construction and D5's allowlist means Host can
/// never invoke an unapproved tool that might misbehave on purpose) and the Ambiguous
/// parser/mapper unit test (an "Ambiguous name" Web end-to-end scenario is infeasible under
/// D5/D7 — get_customer can only ever be invoked with a customerId argument, so
/// CustomerLookupResult.Ambiguous can never actually arise through the P0-05 chat surface; this
/// defensive mapping is forward-compatible code, tested here only).
/// </summary>
public class McpToolResultParserTests
{
    private static CallToolResult ResultWithText(string text, bool? isError = null) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = isError,
    };

    [Fact]
    public void Parse_McpLevelIsError_ReturnsMcpProtocolError()
    {
        var result = McpToolResultParser.Parse(ResultWithText("{}", isError: true));

        Assert.False(result.IsSuccess);
        Assert.Equal(ChatTurnErrorCode.McpProtocolError, result.Error?.Code);
    }

    [Fact]
    public void Parse_NonTextContent_ReturnsMcpInvalidResponse()
    {
        var result = McpToolResultParser.Parse(new CallToolResult { Content = [] });

        Assert.False(result.IsSuccess);
        Assert.Equal(ChatTurnErrorCode.McpInvalidResponse, result.Error?.Code);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsMcpInvalidResponse()
    {
        var result = McpToolResultParser.Parse(ResultWithText("{not valid json"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ChatTurnErrorCode.McpInvalidResponse, result.Error?.Code);
    }

    [Theory]
    [InlineData("""{"traceId":"t1"}""")] // missing status
    [InlineData("""{"status":"success"}""")] // missing traceId
    [InlineData("""{"status":"error","traceId":"t1","error":{"code":"X"}}""")] // error missing message
    [InlineData("""{"status":"error","traceId":"t1","error":"not-an-object"}""")] // error wrong shape
    public void Parse_EnvelopeShapeDrift_ReturnsMcpInvalidResponse(string json)
    {
        var result = McpToolResultParser.Parse(ResultWithText(json));

        Assert.False(result.IsSuccess);
        Assert.Equal(ChatTurnErrorCode.McpInvalidResponse, result.Error?.Code);
    }

    [Fact]
    public void Parse_WellFormedSuccessEnvelope_Succeeds()
    {
        var json = """{"status":"success","traceId":"t1","sourceIds":["crm:customer:CUS-0001"],"data":{"customer":{"id":"CUS-0001"}},"error":null}""";

        var result = McpToolResultParser.Parse(ResultWithText(json));

        Assert.True(result.IsSuccess);
        Assert.Equal("success", result.Result!.Status);
        Assert.Equal("t1", result.Result.TraceId);
        Assert.Contains("crm:customer:CUS-0001", result.Result.SourceIds);
        Assert.True(result.Result.Data.HasValue);
    }

    [Fact]
    public void ToDeterministicChatResponse_Ambiguous_MapsCandidatesAndHttp409Status()
    {
        var json = """
            {"status":"ambiguous","traceId":"t1","sourceIds":[],
             "data":{"candidates":[{"id":"CUS-0002","fullName":"Trần Thị Hương","segment":"Priority","city":"Hà Nội"}]},
             "error":null}
            """;
        var parsed = McpToolResultParser.Parse(ResultWithText(json)).Result!;

        var response = McpToolResultParser.ToDeterministicChatResponse(parsed, [], []);

        Assert.Equal(ChatTurnStatus.Ambiguous, response.Status);
        Assert.Null(response.Error);
        Assert.Single(response.Data!.CustomerCandidates!);
        Assert.Equal("CUS-0002", response.Data.CustomerCandidates![0].Id);
        Assert.Null(response.Data.Customer);
        Assert.Equal(409, ChatEndpoints.MapToHttpStatus(response));
    }

    [Fact]
    public void ToDeterministicChatResponse_NotFound_PreservesSourceIdsButNeverCarriesAccumulatedCrmData()
    {
        var json = """{"status":"not_found","traceId":"t1","sourceIds":[],"error":{"code":"NOT_FOUND","message":"Không tìm thấy.","retryable":false}}""";
        var parsed = McpToolResultParser.Parse(ResultWithText(json)).Result!;

        var response = McpToolResultParser.ToDeterministicChatResponse(parsed, ["crm:customer:CUS-0001"], []);

        Assert.Equal(ChatTurnStatus.NotFound, response.Status);
        Assert.Equal("NOT_FOUND", response.Error?.Code);
        Assert.Contains("crm:customer:CUS-0001", response.SourceIds);
        // Live P0-05 acceptance finding: Data must never carry a prior successful call's raw CRM
        // DTO on a controlled error/not-found outcome.
        Assert.Null(response.Data);
    }

    [Fact]
    public void ExtractCandidates_RoundTripsCustomerCandidateShape()
    {
        using var document = JsonDocument.Parse("""{"candidates":[{"id":"CUS-0002","fullName":"Trần Thị Hương","segment":"Standard","city":"Đà Nẵng"}]}""");

        var candidates = McpToolResultParser.ExtractCandidates(document.RootElement.Clone());

        Assert.Single(candidates!);
        Assert.Equal("CUS-0002", candidates![0].Id);
        Assert.Equal("Standard", candidates[0].Segment);
    }

    [Fact]
    public void ExtractEmailDraft_RoundTripsEmailDraftShape()
    {
        using var document = JsonDocument.Parse("""
            {"draft":{"subject":"S","body":"B","suggestedProductCode":"PRD-SAV-006M",
             "sourceIds":["kb:product:PRD-SAV-006M"],"requiresHumanApproval":true,
             "piiMaskSummary":{"maskedFieldTypes":["name","email","phone","accountReference"]}}}
            """);

        var draft = McpToolResultParser.ExtractEmailDraft(document.RootElement.Clone());

        Assert.NotNull(draft);
        Assert.Equal("PRD-SAV-006M", draft!.SuggestedProductCode);
        Assert.True(draft.RequiresHumanApproval);
        Assert.Contains("name", draft.PiiMaskSummary.MaskedFieldTypes);
    }
}
