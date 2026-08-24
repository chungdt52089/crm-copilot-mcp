using System.Diagnostics;
using System.Text.Json;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Mcp;
using Google.GenAI.Types;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using GenAiTool = Google.GenAI.Types.Tool;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// P0-05 bounded Gemini tool-calling loop (plan §6), extended in P0-06 with conversation-state
/// resolution (docs/02_ARCHITECTURE.md §6). The cap (<see cref="MaxMcpCalls"/> = 3) applies only to
/// MCP tool calls — Gemini may be called one more time than that (up to 4) so it can see the 3rd
/// tool's result and decide whether it's done. No PII round-trips to Gemini (plan D1): every
/// non-success MCP result short-circuits the loop deterministically; every success result's
/// FunctionResponse back to Gemini is minimized (customer/interaction tools) or the full non-PII
/// knowledge content (search_product_knowledge).
///
/// P0-06: a resolvable follow-up ("khách hàng này") never depends on Gemini's own judgement for
/// correctness — the active <c>CurrentCustomerId</c> is both hinted to Gemini via the system
/// instruction AND deterministically substituted into a get_customer/get_interactions call's
/// arguments whenever the model's own call omits <c>customerId</c>, so T06 stays deterministic
/// regardless of the model's exact wording.
/// </summary>
internal sealed class ChatOrchestrator(
    IGeminiChatClient chatClient,
    IMcpClientProvider mcpClientProvider,
    IConversationStateStore stateStore,
    ILogger<ChatOrchestrator> logger)
{
    private const int MaxMcpCalls = 3;
    private const int MaxRecentMessages = 8;

    private const string SystemInstructionText =
        "Bạn là trợ lý CRM tiếng Việt dành cho Relationship Manager (RM). " +
        "Chỉ sử dụng các tool được cung cấp để tra cứu thông tin; không tự bịa thông tin khách hàng " +
        "hoặc sản phẩm. Nếu không có đủ bằng chứng, hãy trả lời trung thực là không tìm thấy. " +
        "Khi một tool đã trả về status \"success\", coi bước đó đã hoàn tất — không gọi lại đúng tool " +
        "với đúng tham số đó lần nữa; hãy dùng thông tin đã có (customerId, sourceIds, số lượng kết " +
        "quả) để trả lời trực tiếp. " +
        "Luôn trả lời bằng tiếng Việt, ngắn gọn, chuyên nghiệp.";

    private const string McpUnavailableMessage = "Không thể kết nối tới MCP Server.";
    private const string ModelErrorMessage = "Không thể tạo phản hồi từ mô hình AI.";
    private const string UnknownToolMessage = "Yêu cầu công cụ không hợp lệ.";
    private const string DuplicateToolCallMessage = "Yêu cầu công cụ bị lặp lại trong cùng một lượt.";
    private const string MultipleFunctionCallsMessage = "Không hỗ trợ gọi nhiều công cụ cùng lúc trong một lượt.";
    private const string ToolLoopLimitMessage = "Đã đạt giới hạn số lần gọi công cụ cho lượt này.";
    private const string NameLookupNotSupportedMessage =
        "Tra cứu khách hàng qua chat chỉ hỗ trợ theo mã khách hàng (ví dụ CUS-0001), không hỗ trợ theo tên.";

    public async Task<ChatResponse> HandleAsync(string sessionId, string message, CancellationToken cancellationToken)
    {
        if (!SessionIdValidator.TryNormalize(sessionId, out var normalizedSessionId))
        {
            return Error(ChatTurnErrorCode.InvalidArgument, SessionIdValidator.InvalidSessionIdMessage, retryable: false);
        }

        var state = stateStore.GetOrCreate(normalizedSessionId);

        var guardResult = InputGuard.Validate(message, state.CurrentCustomerId);
        if (!guardResult.IsAllowed)
        {
            return Error(guardResult.ErrorCode!, guardResult.ErrorMessage!, retryable: false);
        }

        state = stateStore.Update(normalizedSessionId, s => AppendMessage(s, message));

        McpClient client;
        IList<McpClientTool> discovered;
        try
        {
            client = await mcpClientProvider.GetClientAsync(cancellationToken).ConfigureAwait(false);
            discovered = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller's own cancellation — never remapped
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MCP handshake/discovery failed");
            return Error(ChatTurnErrorCode.McpUnavailable, McpUnavailableMessage, retryable: true);
        }

        // D5: Gemini only ever sees the intersection of what the server exposes and what P0-05
        // approved — a tool outside ApprovedMcpToolNames.All is structurally invisible below.
        var exposedTools = discovered.Where(tool => ApprovedMcpToolNames.All.Contains(tool.Name)).ToList();
        var toolsByName = exposedTools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var functionDeclarations = exposedTools.Select(McpToolSchemaMapper.ToFunctionDeclaration).ToList();

        var contents = new List<Content>
        {
            new() { Role = "user", Parts = [Part.FromText(message)] },
        };
        var config = new GenerateContentConfig
        {
            SystemInstruction = new Content { Parts = [Part.FromText(BuildSystemInstruction(state.CurrentCustomerId))] },
            Tools = [new GenAiTool { FunctionDeclarations = functionDeclarations }],
            Temperature = 0.1f,
        };

        var trace = new List<ChatToolTraceEntry>();
        var seenCalls = new HashSet<string>(StringComparer.Ordinal);
        var sourceIds = new List<string>();
        var accumulatedData = new ChatResponseData(null, null, null, null);
        var mcpCallCount = 0;

        while (true)
        {
            GenerateContentResponse response;
            try
            {
                response = await chatClient.GenerateAsync(contents, config, cancellationToken).ConfigureAwait(false);
            }
            catch (ChatModelException ex)
            {
                logger.LogError(ex, "Gemini generateContent failed");
                return Error(ChatTurnErrorCode.ModelError, ModelErrorMessage, ex.Retryable, sourceIds, trace);
            }

            var functionCalls = response.FunctionCalls;
            if (functionCalls is not { Count: > 0 })
            {
                if (response.Text is not { Length: > 0 } replyText)
                {
                    logger.LogError("Gemini response had no function call and no text");
                    return Error(ChatTurnErrorCode.ModelError, ModelErrorMessage, retryable: true, sourceIds, trace);
                }

                return new ChatResponse(replyText, ChatTurnStatus.Success, sourceIds, trace, accumulatedData, null);
            }

            if (functionCalls.Count > 1)
            {
                return Error(ChatTurnErrorCode.MultipleFunctionCallsNotSupported, MultipleFunctionCallsMessage, retryable: false, sourceIds, trace);
            }

            var call = functionCalls[0];
            var callName = call.Name ?? string.Empty;
            var callArgs = call.Args;

            if (!toolsByName.ContainsKey(callName))
            {
                return Error(ChatTurnErrorCode.UnknownTool, UnknownToolMessage, retryable: false, sourceIds, trace);
            }

            // P0-06 deterministic resolution: if this call needs a customerId and Gemini's own
            // call omitted it, fill it in from the session's active customer before any downstream
            // check/dispatch sees the args — correctness never depends on Gemini's wording.
            if (RequiresCustomerId(callName) && !TryGetString(callArgs, "customerId", out _) &&
                state.CurrentCustomerId is { } fallbackCustomerId)
            {
                callArgs = WithCustomerId(callArgs, fallbackCustomerId);
            }

            if (callName == ApprovedMcpToolNames.GetCustomer && !TryGetString(callArgs, "customerId", out _))
            {
                return Error(ChatTurnErrorCode.NameLookupNotSupported, NameLookupNotSupportedMessage, retryable: false, sourceIds, trace);
            }

            if (!seenCalls.Add(CanonicalizeCall(callName, callArgs)))
            {
                return Error(ChatTurnErrorCode.DuplicateToolCall, DuplicateToolCallMessage, retryable: false, sourceIds, trace);
            }

            if (mcpCallCount == MaxMcpCalls)
            {
                return Error(ChatTurnErrorCode.ToolLoopLimitExceeded, ToolLoopLimitMessage, retryable: false, sourceIds, trace);
            }

            var modelContent = response.Candidates is { Count: > 0 } candidates ? candidates[0].Content : null;
            if (modelContent is null)
            {
                return Error(ChatTurnErrorCode.ModelError, ModelErrorMessage, retryable: true, sourceIds, trace);
            }
            contents.Add(modelContent);

            var stopwatch = Stopwatch.StartNew();
            CallToolResult callToolResult;
            try
            {
                callToolResult = await client.CallToolAsync(callName, ToArgumentDictionary(callArgs), cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MCP CallToolAsync failed for {ToolName}", callName);
                return Error(ChatTurnErrorCode.McpUnavailable, McpUnavailableMessage, retryable: true, sourceIds, trace);
            }
            stopwatch.Stop();
            mcpCallCount++;

            var parseResult = McpToolResultParser.Parse(callToolResult);
            if (!parseResult.IsSuccess)
            {
                trace.Add(new ChatToolTraceEntry(callName, "error", "-", stopwatch.ElapsedMilliseconds));
                // Data is intentionally null here, not accumulatedData — a controlled error must
                // never carry a prior successful call's raw CRM DTO (PII) in the same turn.
                return new ChatResponse(null, ChatTurnStatus.Error, sourceIds, trace, null, parseResult.Error);
            }

            var parsed = parseResult.Result!;
            trace.Add(new ChatToolTraceEntry(callName, parsed.Status, parsed.TraceId, stopwatch.ElapsedMilliseconds));

            if (parsed.Status != McpToolStatus.Success)
            {
                return McpToolResultParser.ToDeterministicChatResponse(parsed, sourceIds, trace);
            }

            sourceIds.AddRange(parsed.SourceIds);
            accumulatedData = MergeData(accumulatedData, callName, parsed.Data);
            state = stateStore.Update(normalizedSessionId, s => UpdateStateAfterToolCall(s, callName, callArgs, accumulatedData));

            var minimized = Minimize(callName, callArgs, parsed);
            contents.Add(new Content { Role = "user", Parts = [Part.FromFunctionResponse(callName, minimized)] });
        }
    }

    private static string BuildSystemInstruction(string? currentCustomerId) =>
        currentCustomerId is null
            ? SystemInstructionText
            : SystemInstructionText +
              $" Khách hàng đang được thảo luận trong phiên hội thoại này có mã {currentCustomerId}; " +
              "nếu người dùng dùng cụm như \"khách hàng này\" hoặc không nêu lại mã khách hàng, hãy dùng đúng mã này khi gọi tool.";

    private static bool RequiresCustomerId(string toolName) =>
        toolName is ApprovedMcpToolNames.GetCustomer or ApprovedMcpToolNames.GetInteractions;

    private static Dictionary<string, object> WithCustomerId(IDictionary<string, object>? args, string customerId)
    {
        var merged = args is null ? new Dictionary<string, object>() : new Dictionary<string, object>(args);
        merged["customerId"] = customerId;
        return merged;
    }

    /// <summary>P0-06: redact-then-append (docs/02_ARCHITECTURE.md §6 — no raw email/phone/account
    /// in stored state), keeping only the newest <see cref="MaxRecentMessages"/> entries.</summary>
    private static ConversationState AppendMessage(ConversationState state, string rawMessage)
    {
        var sanitized = ConversationMessageSanitizer.Sanitize(rawMessage);
        var updated = new List<string>(state.RecentSanitizedMessages) { sanitized };
        if (updated.Count > MaxRecentMessages)
        {
            updated.RemoveAt(0);
        }
        return state with { RecentSanitizedMessages = updated, UpdatedAtUtc = DateTime.UtcNow };
    }

    private static ConversationState UpdateStateAfterToolCall(
        ConversationState state, string toolName, IDictionary<string, object>? callArgs, ChatResponseData accumulatedData)
    {
        var now = DateTime.UtcNow;

        if (toolName == ApprovedMcpToolNames.GetCustomer && accumulatedData.Customer is { } customer)
        {
            return state with { CurrentCustomerId = customer.Id, LastIntent = toolName, UpdatedAtUtc = now };
        }

        if (toolName == ApprovedMcpToolNames.GetInteractions && TryGetString(callArgs, "customerId", out var customerId))
        {
            return state with
            {
                CurrentCustomerId = customerId,
                LastInteractionIds = accumulatedData.Interactions?.Select(interaction => interaction.Id).ToList()
                    ?? state.LastInteractionIds,
                LastIntent = toolName,
                UpdatedAtUtc = now,
            };
        }

        if (toolName == ApprovedMcpToolNames.SearchProductKnowledge)
        {
            return state with
            {
                RetrievedSourceIds = accumulatedData.KnowledgeMatches?.Select(match => match.SourceId).ToList()
                    ?? state.RetrievedSourceIds,
                LastIntent = toolName,
                UpdatedAtUtc = now,
            };
        }

        return state;
    }

    private static ChatResponseData MergeData(ChatResponseData accumulated, string toolName, JsonElement? data) => toolName switch
    {
        ApprovedMcpToolNames.GetCustomer => accumulated with { Customer = McpToolResultParser.ExtractCustomer(data) },
        ApprovedMcpToolNames.GetInteractions => accumulated with { Interactions = McpToolResultParser.ExtractInteractions(data) },
        ApprovedMcpToolNames.SearchProductKnowledge => accumulated with { KnowledgeMatches = McpToolResultParser.ExtractKnowledgeMatches(data) },
        _ => accumulated,
    };

    /// <summary>Plan D1: get_customer/get_interactions get a status+sourceIds acknowledgment plus
    /// the non-PII customerId/result-count already implied by sourceIds — never the DTO itself
    /// (name/email/phone/account/city never reach Gemini). The bare status+sourceIds version this
    /// method originally sent was insufficient for Gemini to recognize the step as complete (live
    /// P0-05 acceptance finding: the model re-requested the identical successful call rather than
    /// producing a final reply) — customerId is not itself PII (it's the same synthetic reference
    /// id already embedded in every sourceId string; docs/08 §6's masked-field list is
    /// fullName/email/phone/accountReference/CCCD/address, not the record id).
    /// search_product_knowledge's full non-PII match content is passed through unchanged so Gemini
    /// can ground its reply in it.</summary>
    private static Dictionary<string, object> Minimize(string toolName, IDictionary<string, object>? callArgs, ParsedMcpResult parsed)
    {
        var payload = new Dictionary<string, object>
        {
            ["status"] = "success",
            ["sourceIds"] = parsed.SourceIds,
        };

        if ((toolName == ApprovedMcpToolNames.GetCustomer || toolName == ApprovedMcpToolNames.GetInteractions) &&
            TryGetString(callArgs, "customerId", out var customerId))
        {
            payload["customerId"] = customerId!;
        }

        if (toolName == ApprovedMcpToolNames.GetInteractions)
        {
            payload["interactionCount"] = parsed.SourceIds.Count;
        }

        if (toolName == ApprovedMcpToolNames.SearchProductKnowledge)
        {
            var matches = McpToolResultParser.ExtractKnowledgeMatches(parsed.Data) ?? [];
            payload["matches"] = matches.Select(match => new Dictionary<string, object>
            {
                ["sourceId"] = match.SourceId,
                ["documentType"] = match.DocumentType,
                ["productCode"] = match.ProductCode ?? string.Empty,
                ["content"] = match.Content,
                ["distance"] = match.Distance,
            }).ToList();
        }

        return payload;
    }

    private static bool TryGetString(IDictionary<string, object>? args, string key, out string? value)
    {
        value = null;
        if (args is null || !args.TryGetValue(key, out var raw))
        {
            return false;
        }

        value = raw switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string CanonicalizeCall(string name, IDictionary<string, object>? args)
    {
        if (args is null || args.Count == 0)
        {
            return name + "()";
        }

        var sortedPairs = args.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={Stringify(pair.Value)}");
        return $"{name}({string.Join(",", sortedPairs)})";
    }

    private static string Stringify(object? value) => value switch
    {
        null => "null",
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(value),
    };

    private static Dictionary<string, object?> ToArgumentDictionary(IDictionary<string, object>? args) =>
        args is null ? [] : args.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);

    /// <summary>Data is deliberately not a parameter here: a controlled error response must never
    /// carry a prior successful call's raw CRM DTO (PII) from the same turn (live P0-05
    /// acceptance finding — DUPLICATE_TOOL_CALL was returning a partial CustomerDto with
    /// FullName/Email/Phone/AccountReference/City). sourceIds/trace are not PII (they are already
    /// present in every MCP tool result and in the request's own arguments) and are kept for
    /// observability.</summary>
    private static ChatResponse Error(
        string code, string message, bool retryable,
        IReadOnlyList<string>? sourceIds = null, IReadOnlyList<ChatToolTraceEntry>? trace = null) =>
        new(null, ChatTurnStatus.Error, sourceIds ?? [], trace ?? [], null, new ChatTurnError(code, message, retryable));
}
