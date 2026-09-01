using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Crm;
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

    /// <summary>Mirrors the objective limit both generator tools enforce, so folding dropped text
    /// into the objective can never overflow it (see <see cref="FoldIntoObjective"/>).</summary>
    private const int MaxObjectiveLength = 500;

    /// <summary>Mirrors generate_call_script's own opportunityId shape rule.</summary>
    private static readonly Regex OpportunityIdPattern = new(@"^OPP-\d{4}$", RegexOptions.Compiled);

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

    public async Task<ChatResponse> HandleAsync(string userId, string sessionId, string message, CancellationToken cancellationToken)
    {
        if (!SessionIdValidator.TryNormalize(sessionId, out var normalizedSessionId))
        {
            return Error(ChatTurnErrorCode.InvalidArgument, SessionIdValidator.InvalidSessionIdMessage, retryable: false);
        }

        var state = stateStore.GetOrCreate(userId, normalizedSessionId);

        var guardResult = InputGuard.Validate(message, state.CurrentCustomerId);
        if (!guardResult.IsAllowed)
        {
            return Error(guardResult.ErrorCode!, guardResult.ErrorMessage!, retryable: false);
        }

        state = stateStore.Update(userId, normalizedSessionId, s => AppendMessage(s, message));

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
        var accumulatedData = new ChatResponseData(null, null, null, null, null);
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
                var collapsedCall = TryCollapseToGenerateEmail(functionCalls);

                // Logged for BOTH outcomes, before the decision is acted on, so a parallel-call
                // batch always leaves runtime evidence (the P0-08 live failure left none). Tool
                // names only — a fixed 4-value vocabulary — never arguments, customerId, model
                // text, or any other payload.
                logger.LogWarning(
                    "Gemini returned {FunctionCallCount} parallel function calls {RequestedToolNames}; resolution={Resolution}",
                    functionCalls.Count,
                    string.Join(",", functionCalls.Select(functionCall => functionCall.Name ?? "(unnamed)")),
                    collapsedCall is null ? "Reject" : "CollapseToGenerateEmail");

                if (collapsedCall is null)
                {
                    return Error(ChatTurnErrorCode.MultipleFunctionCallsNotSupported, MultipleFunctionCallsMessage, retryable: false, sourceIds, trace);
                }

                functionCalls = [collapsedCall];
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
            // The query check is load-bearing, not defensive tidying (browser-verified P0-10): given
            // an unrecognized token the model may call get_customer with a `query` instead of a
            // `customerId`. Injecting the session id alongside it produced a call carrying BOTH
            // arguments, which get_customer correctly refuses — surfacing its internal validator
            // message ("Chỉ được cung cấp một trong customerId hoặc query") to the RM. The tool was
            // right; the Host was building an invalid call. Never combine the two.
            if (RequiresCustomerId(callName) && !TryGetString(callArgs, "customerId", out _) &&
                !TryGetString(callArgs, "query", out _) &&
                state.CurrentCustomerId is { } fallbackCustomerId)
            {
                callArgs = WithCustomerId(callArgs, fallbackCustomerId);
            }

            if (callName == ApprovedMcpToolNames.GetCustomer && !TryGetString(callArgs, "customerId", out _))
            {
                return Error(ChatTurnErrorCode.NameLookupNotSupported, NameLookupNotSupportedMessage, retryable: false, sourceIds, trace);
            }

            callArgs = NormalizeIdentifierArguments(callName, callArgs);

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
                return McpToolResultParser.ToDeterministicChatResponse(
                    parsed, sourceIds, trace, BuildDeterministicNotFoundReply(callName, parsed, callArgs, state));
            }

            sourceIds.AddRange(parsed.SourceIds);
            accumulatedData = MergeData(accumulatedData, callName, parsed.Data);
            state = stateStore.Update(userId, normalizedSessionId, s => UpdateStateAfterToolCall(s, callName, callArgs, accumulatedData));

            // P0-08 terminal-tool rule: a successful structured CRM tool ends the turn here — no
            // FunctionResponse is sent back and no further Gemini completion is requested.
            if (IsTerminalStructuredTool(callName))
            {
                return new ChatResponse(
                    BuildDeterministicReply(callName, accumulatedData, callArgs, state),
                    ChatTurnStatus.Success, sourceIds, trace, accumulatedData, null);
            }

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

    /// <summary>
    /// P0-10: get_campaigns is deliberately included. Its customerId is required, not an optional
    /// filter — a campaign lookup is always scoped to one customer (plan D10) — so "chiến dịch của
    /// khách hàng này" must resolve from session state exactly like the other customer-scoped
    /// tools, and a session with no active customer must fail with CUSTOMER_ID_REQUIRED rather than
    /// quietly widening into a list-everything query.
    /// </summary>
    private static bool RequiresCustomerId(string toolName) =>
        toolName is ApprovedMcpToolNames.GetCustomer or ApprovedMcpToolNames.GetInteractions
            or ApprovedMcpToolNames.GenerateEmail or ApprovedMcpToolNames.GetOpportunities
            or ApprovedMcpToolNames.GetCampaigns or ApprovedMcpToolNames.GenerateCallScript;

    /// <summary>
    /// P0-08 live finding (turn 3): <c>generate_email</c> already performs its own nested retrieval
    /// inside EmailTools — it fetches the customer's recent interactions and retrieves both product
    /// and email-template knowledge itself, and only trusts the allowed source ids it built from
    /// that retrieval. A batch pairing it with one or more outer <c>search_product_knowledge</c>
    /// calls is therefore a redundant plan, not a genuine parallel request: the outer search's
    /// result could never reach the draft anyway. Exactly that one shape collapses to the
    /// <c>generate_email</c> call alone (order-independent, outer searches never dispatched).
    ///
    /// Deliberately narrow: more than one <c>generate_email</c>, or any other tool anywhere in the
    /// batch, still returns MULTIPLE_FUNCTION_CALLS_NOT_SUPPORTED. Returns null when the batch is
    /// not collapsible.
    /// </summary>
    private static FunctionCall? TryCollapseToGenerateEmail(IReadOnlyList<FunctionCall> functionCalls)
    {
        FunctionCall? generateEmailCall = null;

        foreach (var candidate in functionCalls)
        {
            var candidateName = candidate.Name ?? string.Empty;

            if (candidateName == ApprovedMcpToolNames.GenerateEmail)
            {
                if (generateEmailCall is not null)
                {
                    return null; // more than one generate_email — not the collapsible shape
                }

                generateEmailCall = candidate;
            }
            else if (candidateName != ApprovedMcpToolNames.SearchProductKnowledge)
            {
                return null; // any other tool present — never collapsed
            }
        }

        return generateEmailCall;
    }

    /// <summary>
    /// The structured CRM tools whose successful result ends the turn immediately (P0-08, extended
    /// in P0-10). <c>search_product_knowledge</c> is deliberately excluded — see
    /// <see cref="BuildDeterministicReply"/>. Because every one of these is terminal,
    /// <see cref="Minimize"/> is never reached for them: no result of theirs is ever fed back into
    /// Gemini's context.
    /// </summary>
    private static bool IsTerminalStructuredTool(string toolName) =>
        toolName is ApprovedMcpToolNames.GetCustomer or ApprovedMcpToolNames.GetInteractions
            or ApprovedMcpToolNames.GenerateEmail or ApprovedMcpToolNames.GetOpportunities
            or ApprovedMcpToolNames.GetCampaigns or ApprovedMcpToolNames.GenerateCallScript
            or ApprovedMcpToolNames.DeleteCustomer;

    /// <summary>
    /// P0-08 live acceptance finding: <see cref="Minimize"/> deliberately strips every semantic
    /// field (interaction Summary/Type/Outcome, customer FullName) before anything reaches Gemini
    /// (plan D1), so for these three tools the model has no grounded content to narrate an answer
    /// from. Asked to produce one anyway, it fabricated both a customer name and an entire product
    /// topic that contradicted the structured panels — and then requested a further redundant tool
    /// call trying to fill the gap. The masking itself held (the real name never reached Gemini);
    /// the defect was asking the model to author prose about data it could not see.
    ///
    /// So the Host composes the reply itself, deterministically, from non-PII facts only
    /// (customerId — the same synthetic reference id already embedded in every sourceId — plus
    /// counts). FullName/Email/Phone/AccountReference, interaction summaries and the email
    /// subject/body are never included: the structured cards are the display surface for the data.
    ///
    /// <c>search_product_knowledge</c> is deliberately NOT terminal — its content carries no
    /// customer PII and IS passed to Gemini in full, so the model's prose there is genuinely
    /// grounded and stays in use.
    /// </summary>
    /// <summary>
    /// P0-08 live finding: looking up a non-existent id mid-conversation returned Reply=null, so the
    /// UI showed only a generic "Không tìm thấy." banner beside the previous customer's still-populated
    /// panels — leaving it ambiguous whose data was on screen. Naming the id that was actually looked
    /// up removes that ambiguity (customerId is a synthetic reference id, not PII — the same value
    /// already embedded in every sourceId).
    ///
    /// A RAG no-evidence outcome is deliberately worded differently: it is also <c>not_found</c>, but
    /// it means "no product/template evidence", not "this customer does not exist". It is told apart
    /// structurally, not by wording — McpToolResponses.RagNoEvidence carries no error object at all,
    /// whereas a genuine customer miss carries NOT_FOUND.
    ///
    /// Returns null for every other non-success outcome, leaving the existing error/ambiguous
    /// behaviour untouched. <see cref="ConversationState.CurrentCustomerId"/> is deliberately NOT
    /// cleared by a failed lookup, so follow-ups continue to resolve against the last customer that
    /// actually loaded.
    /// </summary>
    private static string? BuildDeterministicNotFoundReply(
        string toolName, ParsedMcpResult parsed, IDictionary<string, object>? callArgs, ConversationState state)
    {
        if (parsed.Status != McpToolStatus.NotFound || !IsTerminalStructuredTool(toolName))
        {
            return null;
        }

        var customerId = (TryGetString(callArgs, "customerId", out var argCustomerId) ? argCustomerId : null)
            ?? state.CurrentCustomerId;
        var customerLabel = customerId is { Length: > 0 } ? $"khách hàng {customerId}" : "khách hàng được yêu cầu";

        if (parsed.Error is not null)
        {
            return $"Không tìm thấy {customerLabel}.";
        }

        // Error == null on a not_found is McpToolResponses.RagNoEvidence: the entity exists, the
        // grounding evidence does not. Worded per generator so the RM can tell "no product/template
        // evidence" apart from "no customer".
        return toolName == ApprovedMcpToolNames.GenerateCallScript
            ? $"Không đủ dữ liệu sản phẩm hoặc kịch bản gọi phù hợp để soạn kịch bản cho {customerLabel}."
            : $"Không đủ dữ liệu sản phẩm hoặc mẫu email phù hợp để soạn email cho {customerLabel}.";
    }

    private static string BuildDeterministicReply(
        string toolName, ChatResponseData data, IDictionary<string, object>? callArgs, ConversationState state)
    {
        var customerId = data.Customer?.Id
            ?? (TryGetString(callArgs, "customerId", out var argCustomerId) ? argCustomerId : null)
            ?? state.CurrentCustomerId;
        var customerLabel = customerId is { Length: > 0 } ? $"khách hàng {customerId}" : "khách hàng hiện tại";

        return toolName switch
        {
            ApprovedMcpToolNames.GetCustomer =>
                $"Đã tải hồ sơ {customerLabel}. Xem dữ liệu chi tiết bên dưới.",
            ApprovedMcpToolNames.GetInteractions =>
                $"Đã tải {data.Interactions?.Count ?? 0} tương tác gần nhất của {customerLabel}. Xem dữ liệu chi tiết bên dưới.",
            ApprovedMcpToolNames.GetOpportunities =>
                $"Đã tải {data.Opportunities?.Count ?? 0} cơ hội bán của {customerLabel}. Xem dữ liệu chi tiết bên dưới.",
            ApprovedMcpToolNames.GetCampaigns =>
                $"Đã tải {data.Campaigns?.Count ?? 0} chiến dịch mà {customerLabel} thuộc diện tham gia. Xem dữ liệu chi tiết bên dưới.",
            ApprovedMcpToolNames.GenerateCallScript =>
                $"Đã tạo kịch bản gọi cho {customerLabel}. Kịch bản cần RM kiểm tra và phê duyệt.",

            // P0-14. This arm must exist explicitly: the `_` fallback below means generate_email, so
            // without it a successful delete would report "Đã tạo email nháp cho ...".
            ApprovedMcpToolNames.DeleteCustomer =>
                $"Đã xoá {customerLabel} khỏi hệ thống CRM.",
            _ =>
                $"Đã tạo email nháp cho {customerLabel}. Bản nháp cần RM kiểm tra và phê duyệt.",
        };
    }

    private static Dictionary<string, object> WithCustomerId(IDictionary<string, object>? args, string customerId)
    {
        var merged = args is null ? new Dictionary<string, object>() : new Dictionary<string, object>(args);
        merged["customerId"] = customerId;
        return merged;
    }

    /// <summary>
    /// Browser-verified P0-10 finding: asked in plain Vietnamese ("Soạn email follow-up cho khách
    /// hàng này về gửi tiết kiệm 6 tháng"), Gemini fills <c>productCode</c> with the natural-language
    /// phrase — "gửi tiết kiệm 6 tháng" — rather than a catalogue code. The MCP tool then correctly
    /// answers INVALID_ARGUMENT, so a perfectly reasonable request failed. Nothing was wrong at the
    /// tool boundary: the model simply cannot know the catalogue, and the Host was passing its guess
    /// through unexamined.
    ///
    /// So the Host normalizes identifier-shaped arguments before dispatch. A malformed value is
    /// DROPPED, never repaired and never forwarded — the MCP validator stays exactly as strict, and
    /// a direct MCP call carrying the same bad value still gets INVALID_ARGUMENT.
    ///
    /// The user's intent is not discarded with it: the phrase is folded into <c>objective</c>, where
    /// it belongs, so the tool's own retrieval resolves it to a real product. That is the component
    /// that actually knows the catalogue — mapping text to a product code is what the RAG step is
    /// for, and guessing here would only move the same error one layer up.
    /// </summary>
    private Dictionary<string, object>? NormalizeIdentifierArguments(string toolName, Dictionary<string, object>? args)
    {
        if (args is null || args.Count == 0 ||
            toolName is not (ApprovedMcpToolNames.GenerateEmail or ApprovedMcpToolNames.GenerateCallScript))
        {
            return args;
        }

        var normalized = new Dictionary<string, object>(args);
        var droppedArguments = new List<string>();

        if (TryGetString(normalized, "productCode", out var productCode) && !ProductCodeFormat.IsWellFormed(productCode))
        {
            normalized.Remove("productCode");
            droppedArguments.Add("productCode");
            FoldIntoObjective(normalized, productCode!);
        }

        // generate_call_script only. Same reasoning: an opportunity id the model invented is worse
        // than no opportunity id, because the tool selects one deterministically when none is given.
        if (toolName == ApprovedMcpToolNames.GenerateCallScript &&
            TryGetString(normalized, "opportunityId", out var opportunityId) &&
            !OpportunityIdPattern.IsMatch(opportunityId!))
        {
            normalized.Remove("opportunityId");
            droppedArguments.Add("opportunityId");
        }

        if (droppedArguments.Count == 0)
        {
            return args;
        }

        // Argument NAMES only — a fixed vocabulary. The dropped values are model-authored free text
        // and never logged.
        logger.LogInformation(
            "Dropped malformed model-supplied argument(s) {DroppedArguments} for {ToolName} before MCP dispatch",
            string.Join(",", droppedArguments), toolName);

        return normalized;
    }

    /// <summary>
    /// Preserves the need the model expressed, bounded by the tool's own 500-character objective
    /// limit so folding text in can never turn a valid call into an INVALID_ARGUMENT of its own.
    /// </summary>
    private static void FoldIntoObjective(Dictionary<string, object> args, string droppedText)
    {
        var trimmed = droppedText.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var objective = TryGetString(args, "objective", out var existing) ? existing! : string.Empty;

        if (objective.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return; // the phrase is already stated in the objective — nothing to add
        }

        var combined = objective.Length == 0 ? trimmed : $"{objective} — {trimmed}";
        args["objective"] = combined.Length <= MaxObjectiveLength ? combined : combined[..MaxObjectiveLength];
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

        if (toolName == ApprovedMcpToolNames.GenerateEmail && TryGetString(callArgs, "customerId", out var emailCustomerId))
        {
            return state with { CurrentCustomerId = emailCustomerId, LastIntent = toolName, UpdatedAtUtc = now };
        }

        // P0-10: the three new tools are all customer-scoped, so a successful call establishes the
        // session's active customer exactly as get_interactions/generate_email already do — a
        // follow-up "khách hàng này" after an opportunity lookup must keep resolving.
        if (toolName is ApprovedMcpToolNames.GetOpportunities or ApprovedMcpToolNames.GetCampaigns
                or ApprovedMcpToolNames.GenerateCallScript &&
            TryGetString(callArgs, "customerId", out var scopedCustomerId))
        {
            return state with { CurrentCustomerId = scopedCustomerId, LastIntent = toolName, UpdatedAtUtc = now };
        }

        return state;
    }

    private static ChatResponseData MergeData(ChatResponseData accumulated, string toolName, JsonElement? data) => toolName switch
    {
        ApprovedMcpToolNames.GetCustomer => accumulated with { Customer = McpToolResultParser.ExtractCustomer(data) },
        ApprovedMcpToolNames.GetInteractions => accumulated with { Interactions = McpToolResultParser.ExtractInteractions(data) },
        ApprovedMcpToolNames.SearchProductKnowledge => accumulated with { KnowledgeMatches = McpToolResultParser.ExtractKnowledgeMatches(data) },
        ApprovedMcpToolNames.GenerateEmail => accumulated with { EmailDraft = McpToolResultParser.ExtractEmailDraft(data) },
        ApprovedMcpToolNames.GetOpportunities => accumulated with { Opportunities = McpToolResultParser.ExtractOpportunities(data) },
        ApprovedMcpToolNames.GetCampaigns => accumulated with { Campaigns = McpToolResultParser.ExtractCampaigns(data) },
        ApprovedMcpToolNames.GenerateCallScript => accumulated with { CallScript = McpToolResultParser.ExtractCallScript(data) },
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

        if ((toolName == ApprovedMcpToolNames.GetCustomer || toolName == ApprovedMcpToolNames.GetInteractions ||
             toolName == ApprovedMcpToolNames.GenerateEmail) &&
            TryGetString(callArgs, "customerId", out var customerId))
        {
            payload["customerId"] = customerId!;
        }

        if (toolName == ApprovedMcpToolNames.GetInteractions)
        {
            payload["interactionCount"] = parsed.SourceIds.Count;
        }

        if (toolName == ApprovedMcpToolNames.GenerateEmail)
        {
            // Deliberately excludes Subject/Body: EmailTools already restored the customer's real
            // FullName into both before this result reached the Host (plan D1) — echoing them back
            // into Gemini's own context here would round-trip PII into the model loop a second time.
            var draft = McpToolResultParser.ExtractEmailDraft(parsed.Data);
            payload["requiresHumanApproval"] = draft?.RequiresHumanApproval ?? true;
            payload["suggestedProductCode"] = draft?.SuggestedProductCode ?? string.Empty;
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
