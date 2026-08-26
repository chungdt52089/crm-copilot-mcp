using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Mcp;
using ModelContextProtocol.Protocol;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// Parses an MCP <see cref="CallToolResult"/>'s text content into the P0-04
/// <see cref="McpToolResult"/> wire shape and maps a non-success outcome into a deterministic
/// <see cref="ChatResponse"/> (plan D1/D9). This is the single seam that owns every "the MCP layer
/// said something we didn't expect" case (plan D9's failure table rows for MCP-level IsError,
/// non-text content, malformed JSON, and envelope-shape drift) — none of them ever throws past
/// this type.
/// </summary>
internal static class McpToolResultParser
{
    private const string ProtocolErrorMessage = "MCP tool call trả về lỗi giao thức không mong đợi.";
    private const string InvalidResponseMessage = "Không thể đọc kết quả từ MCP tool.";

    public static McpParseResult Parse(CallToolResult result)
    {
        if (result.IsError == true)
        {
            // Should not happen for the three P0-04 tools (README: "mọi response... đều là một
            // tool result JSON thường... không dùng MCP-level isError cho lỗi nghiệp vụ"), but
            // must not crash if it ever does.
            return McpParseResult.Failure(ChatTurnErrorCode.McpProtocolError, ProtocolErrorMessage, retryable: false);
        }

        if (result.Content.Count != 1 || result.Content[0] is not TextContentBlock textBlock)
        {
            return McpParseResult.Failure(ChatTurnErrorCode.McpInvalidResponse, InvalidResponseMessage, retryable: false);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(textBlock.Text);
        }
        catch (JsonException)
        {
            return McpParseResult.Failure(ChatTurnErrorCode.McpInvalidResponse, InvalidResponseMessage, retryable: false);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("status", out var statusElement) || statusElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("traceId", out var traceIdElement) || traceIdElement.ValueKind != JsonValueKind.String)
            {
                return McpParseResult.Failure(ChatTurnErrorCode.McpInvalidResponse, InvalidResponseMessage, retryable: false);
            }

            var sourceIds = new List<string>();
            if (root.TryGetProperty("sourceIds", out var sourceIdsElement) && sourceIdsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in sourceIdsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        sourceIds.Add(item.GetString()!);
                    }
                }
            }

            McpToolError? error = null;
            if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
            {
                if (!errorElement.TryGetProperty("code", out var codeElement) || codeElement.ValueKind != JsonValueKind.String ||
                    !errorElement.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.String)
                {
                    return McpParseResult.Failure(ChatTurnErrorCode.McpInvalidResponse, InvalidResponseMessage, retryable: false);
                }

                var retryable = errorElement.TryGetProperty("retryable", out var retryableElement) &&
                                 retryableElement.ValueKind == JsonValueKind.True;
                error = new McpToolError(codeElement.GetString()!, messageElement.GetString()!, retryable);
            }
            else if (errorElement.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                return McpParseResult.Failure(ChatTurnErrorCode.McpInvalidResponse, InvalidResponseMessage, retryable: false);
            }

            JsonElement? data = root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind != JsonValueKind.Null
                ? dataElement.Clone()
                : null;

            var parsed = new ParsedMcpResult(statusElement.GetString()!, traceIdElement.GetString()!, sourceIds, data, error);
            return McpParseResult.Success(parsed);
        }
    }

    /// <summary>Deterministic rendering for any non-success status (plan D1) — never called for
    /// <see cref="McpToolStatus.Success"/>, which the orchestrator handles separately (it must
    /// continue the loop, not return immediately). Live P0-05 acceptance finding: a controlled
    /// error/not-found response must never carry a prior successful call's raw CRM DTO (PII) from
    /// earlier in the same turn — so <see cref="ChatResponse.Data"/> is always either null (not
    /// found/error) or a fresh, minimal shape built only from this one parsed result (ambiguous
    /// candidates — already the non-DTO, UI-safe shape per docs/06 §7), never the caller-supplied
    /// accumulated state. <paramref name="sourceIds"/> is not PII (already public inside every MCP
    /// tool result) and is kept for observability.</summary>
    /// <param name="reply">P0-08: an optional Host-authored, deterministic, PII-safe reply for the
    /// not-found case (e.g. naming the customer id that was actually looked up). Never model text.
    /// Ambiguous keeps its own null reply — the candidate list is the answer there.</param>
    public static ChatResponse ToDeterministicChatResponse(
        ParsedMcpResult parsed, IReadOnlyList<string> sourceIds, IReadOnlyList<ChatToolTraceEntry> trace,
        string? reply = null)
    {
        if (parsed.Status == McpToolStatus.Ambiguous)
        {
            var candidates = ExtractCandidates(parsed.Data);
            return new ChatResponse(
                null, ChatTurnStatus.Ambiguous, sourceIds, trace, new ChatResponseData(null, candidates, null, null, null), null);
        }

        var mappedError = parsed.Error is { } e ? new ChatTurnError(e.Code, e.Message, e.Retryable) : null;
        var status = parsed.Status == McpToolStatus.NotFound ? ChatTurnStatus.NotFound : ChatTurnStatus.Error;
        return new ChatResponse(reply, status, sourceIds, trace, null, mappedError);
    }

    public static CustomerDto? ExtractCustomer(JsonElement? data) =>
        Deserialize<GetCustomerFoundData>(data)?.Customer;

    public static IReadOnlyList<CustomerCandidateDto>? ExtractCandidates(JsonElement? data) =>
        Deserialize<GetCustomerAmbiguousData>(data)?.Candidates;

    public static IReadOnlyList<InteractionDto>? ExtractInteractions(JsonElement? data) =>
        Deserialize<GetInteractionsData>(data)?.Interactions;

    public static IReadOnlyList<KnowledgeMatchDto>? ExtractKnowledgeMatches(JsonElement? data) =>
        Deserialize<SearchProductKnowledgeData>(data)?.Matches;

    public static EmailDraftDto? ExtractEmailDraft(JsonElement? data) =>
        Deserialize<GenerateEmailData>(data)?.Draft;

    public static IReadOnlyList<OpportunityDto>? ExtractOpportunities(JsonElement? data) =>
        Deserialize<GetOpportunitiesData>(data)?.Opportunities;

    public static IReadOnlyList<CampaignDto>? ExtractCampaigns(JsonElement? data) =>
        Deserialize<GetCampaignsData>(data)?.Campaigns;

    public static CallScriptDraftDto? ExtractCallScript(JsonElement? data) =>
        Deserialize<GenerateCallScriptData>(data)?.Draft;

    private static T? Deserialize<T>(JsonElement? data) where T : class =>
        data is { } value ? value.Deserialize<T>(CrmJsonOptions.Default) : null;
}

internal sealed record ParsedMcpResult(
    string Status, string TraceId, IReadOnlyList<string> SourceIds, JsonElement? Data, McpToolError? Error);

internal sealed record McpParseResult(bool IsSuccess, ParsedMcpResult? Result, ChatTurnError? Error)
{
    public static McpParseResult Success(ParsedMcpResult result) => new(true, result, null);

    public static McpParseResult Failure(string code, string message, bool retryable) =>
        new(false, null, new ChatTurnError(code, message, retryable));
}
