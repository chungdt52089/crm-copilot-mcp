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

    // --- P0-10 extractors ---------------------------------------------------------------------

    [Fact]
    public void ExtractOpportunities_RoundTripsOpportunityShape()
    {
        using var document = JsonDocument.Parse("""
            {"opportunities":[{"id":"OPP-0002","customerId":"CUS-0001","productCode":"PRD-SAV-006M",
             "stage":"Proposal","amountVnd":250000000,"expectedCloseDateUtc":"2026-09-10T09:00:00Z",
             "status":"Open","synthetic":true}]}
            """);

        var opportunities = McpToolResultParser.ExtractOpportunities(document.RootElement.Clone());

        var opportunity = Assert.Single(opportunities!);
        Assert.Equal("OPP-0002", opportunity.Id);
        Assert.Equal("PRD-SAV-006M", opportunity.ProductCode);
        Assert.Equal("Open", opportunity.Status);
        // The exact figure legitimately reaches the Host/UI over the trusted local path; only the
        // Gemini prompt gets a band instead (plan D12).
        Assert.Equal(250_000_000, opportunity.AmountVnd);
    }

    [Fact]
    public void ExtractCampaigns_RoundTripsCampaignShapeIncludingEligibility()
    {
        using var document = JsonDocument.Parse("""
            {"campaigns":[{"id":"CMP-0001","name":"Ưu đãi tiền gửi","objective":"Giới thiệu",
             "targetSegment":"Priority","productCodes":["PRD-SAV-006M"],
             "eligibleCustomerIds":["CUS-0001","CUS-0002"],
             "startDateUtc":"2026-07-26T09:00:00Z","endDateUtc":"2026-09-29T09:00:00Z",
             "status":"Active","synthetic":true}]}
            """);

        var campaigns = McpToolResultParser.ExtractCampaigns(document.RootElement.Clone());

        var campaign = Assert.Single(campaigns!);
        Assert.Equal("CMP-0001", campaign.Id);
        Assert.Contains("CUS-0001", campaign.EligibleCustomerIds);
        Assert.Contains("PRD-SAV-006M", campaign.ProductCodes);
    }

    [Fact]
    public void ExtractCallScript_RoundTripsAllFiveSectionsAndProvenanceFields()
    {
        using var document = JsonDocument.Parse("""
            {"draft":{"opening":"Kính chào","discoveryQuestions":["Q1","Q2"],
             "talkingPoints":["T1"],
             "objectionHandling":[{"objection":"O1","response":"R1"}],
             "closing":"Cảm ơn","suggestedProductCode":"PRD-SAV-006M",
             "selectedOpportunityId":"OPP-0002","resolvedObjective":"Trao đổi",
             "objectiveSource":"opportunity",
             "sourceIds":["crm:opportunity:OPP-0002","kb:call-script:CS-CALL-SAVINGS-FOLLOWUP-01"],
             "requiresHumanApproval":true,"warnings":["OBJECTIVE_INFERRED"],
             "piiMaskSummary":{"maskedFieldTypes":["name"]}}}
            """);

        var draft = McpToolResultParser.ExtractCallScript(document.RootElement.Clone());

        Assert.NotNull(draft);
        Assert.Equal("Kính chào", draft!.Opening);
        Assert.Equal(2, draft.DiscoveryQuestions.Count);
        Assert.Single(draft.TalkingPoints);
        Assert.Equal("O1", Assert.Single(draft.ObjectionHandling).Objection);
        Assert.Equal("Cảm ơn", draft.Closing);
        Assert.Equal("OPP-0002", draft.SelectedOpportunityId);
        Assert.Equal("opportunity", draft.ObjectiveSource);
        Assert.Contains("OBJECTIVE_INFERRED", draft.Warnings);
        Assert.True(draft.RequiresHumanApproval);
    }

    [Fact]
    public void ExtractCallScript_MalformedData_ReturnsNullRatherThanThrowing()
    {
        using var document = JsonDocument.Parse("""{"unexpected":true}""");

        Assert.Null(McpToolResultParser.ExtractCallScript(document.RootElement.Clone()));
    }
}
