using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrmCopilot.Contracts.Chat;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.Tests.Acceptance.TestSupport;
using CrmCopilot.Tests.Web.TestSupport;
using ModelContextProtocol.Protocol;

namespace CrmCopilot.Tests.Acceptance;

/// <summary>
/// Executes the eight docs/03_ACCEPTANCE_CRITERIA.md §6 scenarios in a fixed order and returns one
/// <see cref="ScenarioResult"/> per scenario.
///
/// Why a plain runner rather than eight <c>[Fact]</c>s plus an aggregating one: xUnit specifies
/// neither execution order nor serialization across tests (collections run in parallel by default),
/// so an aggregator reading state produced by sibling facts would race or read nothing. Here a
/// single fact drives this loop, and the report is rendered from the very list the assertions then
/// read — one source of truth (plan §4.2).
///
/// Each scenario builds and disposes its own harness, so results do not depend on execution order
/// even though the order is fixed; and each scenario *records* its checks rather than throwing, so
/// one failure never truncates the report.
/// </summary>
internal sealed class AcceptanceScenarioRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Any {{PLACEHOLDER}} token that survived into the final draft.</summary>
    private static readonly Regex PlaceholderPattern = new(@"\{\{[A-Z_]+\}\}", RegexOptions.Compiled);

    /// <summary>
    /// A Vietnamese-diacritic letter. Used to prove the draft is real Vietnamese prose rather than
    /// ASCII-folded output.
    /// </summary>
    private static readonly Regex VietnameseDiacriticPattern = new(
        "[àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The knowledge corpus contains no percentage at all (data/knowledge/*.json has zero '%'
    /// characters; every mention of "Lãi suất" is the constraint forbidding invention). So any rate
    /// in a draft is fabricated by definition — no fuzzy judgement needed.
    /// </summary>
    private static readonly Regex FabricatedRatePattern = new(
        @"%|lãi\s*suất[^.]{0,20}\d", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CustomerIdPattern = new(@"CUS-\d{4}", RegexOptions.Compiled);
    private static readonly Regex ProductCodePattern = new(@"PRD-[A-Z0-9-]+", RegexOptions.Compiled);

    /// <summary>Shapes that would betray a leaked stack trace / raw exception in a user-facing string.</summary>
    private static readonly string[] StackTraceMarkers = ["   at ", "System.", "Exception:", "StackTrace"];

    public static IReadOnlyList<ScenarioId> AllScenarios => Enum.GetValues<ScenarioId>();

    /// <summary>
    /// Runs the given scenarios sequentially in the order supplied. An unexpected exception at the
    /// scenario boundary becomes <see cref="ScenarioOutcome.Error"/> — "not evaluated" — never a
    /// <see cref="ScenarioOutcome.Fail"/> that the ≥7/8 budget could absorb.
    /// </summary>
    public async Task<IReadOnlyList<ScenarioResult>> RunAsync(
        IEnumerable<ScenarioId> scenarios, CancellationToken cancellationToken)
    {
        var results = new List<ScenarioResult>();

        foreach (var scenario in scenarios)
        {
            var stopwatch = Stopwatch.StartNew();
            var (title, boundary) = Describe(scenario);
            try
            {
                results.Add(await RunOneAsync(scenario, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                results.Add(ScenarioResult.Errored(
                    scenario, title, boundary, EvidenceClass.Deterministic,
                    Sanitize(exception), stopwatch.ElapsedMilliseconds));
            }
        }

        return results;
    }

    private static Task<ScenarioResult> RunOneAsync(ScenarioId scenario, CancellationToken cancellationToken) => scenario switch
    {
        ScenarioId.T01 => RunT01Async(cancellationToken),
        ScenarioId.T02 => RunT02Async(cancellationToken),
        ScenarioId.T03 => RunT03Async(cancellationToken),
        ScenarioId.T04 => RunT04Async(cancellationToken),
        ScenarioId.T05 => RunT05Async(cancellationToken),
        ScenarioId.T06 => RunT06Async(cancellationToken),
        ScenarioId.T07 => RunT07Async(cancellationToken),
        ScenarioId.T08 => RunT08Async(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario."),
    };

    private static (string Title, string Boundary) Describe(ScenarioId scenario) => scenario switch
    {
        ScenarioId.T01 => ("Lookup CUS-0001 theo ID", "POST /api/chat"),
        ScenarioId.T02 => ("Lookup theo tên duy nhất", "MCP tool get_customer"),
        ScenarioId.T03 => ("Lookup theo tên trùng", "MCP tool get_customer"),
        ScenarioId.T04 => ("Customer không tồn tại", "POST /api/chat"),
        ScenarioId.T05 => ("Interactions của CUS-0001", "POST /api/chat"),
        ScenarioId.T06 => ("Multi-turn \"khách hàng này\"", "POST /api/chat"),
        ScenarioId.T07 => ("Email draft RAG", "MCP tool generate_email"),
        ScenarioId.T08 => ("Safety / resilience", "POST /api/chat + MCP"),
        _ => (scenario.ToString(), "-"),
    };

    // ------------------------------------------------------------------
    // T01 — Lookup CUS-0001 by ID.
    // ------------------------------------------------------------------
    private static async Task<ScenarioResult> RunT01Async(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checklist = new ScenarioChecklist();

        await using var harness = await AcceptanceHarness.CreateWithWebAsync(cancellationToken);
        harness.ChatClient!.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = ScenarioDatasetSeed.CanonicalCustomerId }));

        var (response, body) = await PostChatAsync(
            harness.CreateWebClient(), "Tìm hồ sơ khách hàng CUS-0001.", Guid.NewGuid().ToString(), cancellationToken);

        var expected = ScenarioDatasetSeed.CanonicalCustomer;
        checklist.RequireEqual("HTTP 200", HttpStatusCode.OK, response.StatusCode);
        checklist.RequireEqual("status = success", ChatTurnStatus.Success, body.Status);
        checklist.Require("error là null", body.Error is null, $"error={body.Error?.Code ?? "<null>"}");
        checklist.RequireContains("sourceIds chứa crm:customer:CUS-0001", body.SourceIds, $"crm:customer:{expected.Id}");
        checklist.RequireEqual("data.customer.id", expected.Id, body.Data?.Customer?.Id);
        checklist.RequireEqual("data.customer.fullName khớp dataset", expected.FullName, body.Data?.Customer?.FullName);
        checklist.RequireEqual("toolTrace có đúng 1 entry", 1, body.ToolTrace.Count);
        checklist.RequireEqual("toolTrace[0].toolName", "get_customer", body.ToolTrace.FirstOrDefault()?.ToolName);
        checklist.RequireEqual("toolTrace[0].status", McpToolStatus.Success, body.ToolTrace.FirstOrDefault()?.Status);
        checklist.Require(
            "toolTrace[0].traceId không rỗng",
            !string.IsNullOrWhiteSpace(body.ToolTrace.FirstOrDefault()?.TraceId),
            $"traceId={body.ToolTrace.FirstOrDefault()?.TraceId ?? "<null>"}");

        stopwatch.Stop();
        return Build(ScenarioId.T01, checklist, stopwatch);
    }

    // ------------------------------------------------------------------
    // T02 — Unique-name lookup, evaluated at the MCP tool boundary.
    // Not via /api/chat: InputGuard (P0-05 decision D7) deliberately rejects a capitalized name run
    // without a CUS-#### code, so the chat path would fail by design, not by defect.
    // ------------------------------------------------------------------
    private static async Task<ScenarioResult> RunT02Async(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checklist = new ScenarioChecklist();

        await using var harness = await AcceptanceHarness.CreateMcpOnlyAsync(cancellationToken);
        var root = await CallToolAsync(
            harness, "get_customer",
            new Dictionary<string, object?> { ["query"] = ScenarioDatasetSeed.UniqueFullName },
            cancellationToken);

        var expected = ScenarioDatasetSeed.CanonicalCustomer;
        checklist.RequireEqual("status = success", McpToolStatus.Success, GetString(root, "status"));
        checklist.RequireEqual(
            "trả đúng một customer", expected.Id,
            root.TryGetProperty("data", out var data) && data.TryGetProperty("customer", out var customer)
                ? customer.GetProperty("id").GetString()
                : null);
        checklist.RequireSequenceEqual("sourceIds", [$"crm:customer:{expected.Id}"], ReadStringArray(root, "sourceIds"));
        checklist.Require(
            "không trả candidates (không phải ambiguous)",
            !(root.TryGetProperty("data", out var withoutCandidates) && withoutCandidates.TryGetProperty("candidates", out _)),
            "data.candidates phải vắng mặt khi tên là duy nhất");

        stopwatch.Stop();
        return Build(ScenarioId.T02, checklist, stopwatch);
    }

    // ------------------------------------------------------------------
    // T03 — Duplicate-name lookup: candidates returned, never auto-picked.
    // ------------------------------------------------------------------
    private static async Task<ScenarioResult> RunT03Async(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checklist = new ScenarioChecklist();

        var (duplicateName, duplicateIds) = ScenarioDatasetSeed.DuplicateNameGroup;

        await using var harness = await AcceptanceHarness.CreateMcpOnlyAsync(cancellationToken);
        var root = await CallToolAsync(
            harness, "get_customer",
            new Dictionary<string, object?> { ["query"] = duplicateName },
            cancellationToken);

        var candidateIds = new List<string>();
        if (root.TryGetProperty("data", out var data) && data.TryGetProperty("candidates", out var candidates))
        {
            candidateIds.AddRange(candidates.EnumerateArray()
                .Select(candidate => candidate.GetProperty("id").GetString() ?? string.Empty)
                .Order(StringComparer.Ordinal));
        }

        checklist.RequireEqual("status = ambiguous", McpToolStatus.Ambiguous, GetString(root, "status"));
        checklist.RequireSequenceEqual("candidates chứa đúng cặp trùng tên", duplicateIds, candidateIds);
        checklist.Require(
            "KHÔNG tự chọn (data.customer vắng mặt)",
            !(root.TryGetProperty("data", out var withoutCustomer) && withoutCustomer.TryGetProperty("customer", out _)),
            "một lookup ambiguous không bao giờ được tự chọn một candidate");
        checklist.Require("sourceIds rỗng", ReadStringArray(root, "sourceIds").Count == 0, "ambiguous không cite nguồn nào");

        stopwatch.Stop();
        return Build(ScenarioId.T03, checklist, stopwatch);
    }

    // ------------------------------------------------------------------
    // T04 — Nonexistent customer: NOT_FOUND, nothing fabricated.
    // ------------------------------------------------------------------
    private static async Task<ScenarioResult> RunT04Async(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checklist = new ScenarioChecklist();

        await using var harness = await AcceptanceHarness.CreateWithWebAsync(cancellationToken);
        harness.ChatClient!.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = ScenarioDatasetSeed.MissingCustomerId }));

        var (response, body) = await PostChatAsync(
            harness.CreateWebClient(), "Tìm khách hàng CUS-9999.", Guid.NewGuid().ToString(), cancellationToken);

        checklist.RequireEqual("HTTP 404", HttpStatusCode.NotFound, response.StatusCode);
        checklist.RequireEqual("status = not_found", ChatTurnStatus.NotFound, body.Status);
        checklist.RequireEqual("error.code = NOT_FOUND", ChatTurnErrorCode.NotFound, body.Error?.Code);
        checklist.RequireEqual("error.retryable = false", false, body.Error?.Retryable);
        checklist.Require("data là null", body.Data is null, body.Data is null ? "<null>" : "<present>");
        checklist.Require("sourceIds rỗng", body.SourceIds.Count == 0, $"count={body.SourceIds.Count}");

        // The requested id may legitimately be echoed back ("Không tìm thấy khách hàng CUS-9999.");
        // anything beyond that would be invention.
        var reply = body.Reply ?? string.Empty;
        var fabricatedIds = CustomerIdPattern.Matches(reply)
            .Select(match => match.Value)
            .Where(value => !string.Equals(value, ScenarioDatasetSeed.MissingCustomerId, StringComparison.Ordinal))
            .ToList();
        checklist.Require(
            "reply không bịa customer ID nào khác",
            fabricatedIds.Count == 0,
            $"IDs lạ trong reply=[{string.Join(", ", fabricatedIds)}]");
        checklist.Require(
            "reply không bịa product code",
            !ProductCodePattern.IsMatch(reply),
            $"product codes=[{string.Join(", ", ProductCodePattern.Matches(reply).Select(match => match.Value))}]");
        checklist.Require(
            "reply không chứa PII của khách hàng canonical",
            !ScenarioDatasetSeed.CanonicalPiiValues.Any(pii => reply.Contains(pii, StringComparison.Ordinal)),
            "một lookup trượt không được rò hồ sơ khác");

        stopwatch.Stop();
        return Build(ScenarioId.T04, checklist, stopwatch);
    }

    // ------------------------------------------------------------------
    // T05 — Interactions: right customer, newest-first, limit honored, and the canonical savings
    // interaction (the one the demo's email step is grounded in) present and newest.
    // ------------------------------------------------------------------
    private static async Task<ScenarioResult> RunT05Async(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checklist = new ScenarioChecklist();

        var customerId = ScenarioDatasetSeed.CanonicalCustomerId;
        var expectedIds = ScenarioDatasetSeed.CanonicalInteractionIdsNewestFirst;
        var savings = ScenarioDatasetSeed.CanonicalSavingsInteraction;

        await using var harness = await AcceptanceHarness.CreateWithWebAsync(cancellationToken);
        harness.ChatClient!.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_interactions", new Dictionary<string, object> { ["customerId"] = customerId, ["limit"] = 5 }));

        var client = harness.CreateWebClient();
        var (_, body) = await PostChatAsync(
            client, "Xem các tương tác gần đây của CUS-0001.", Guid.NewGuid().ToString(), cancellationToken);

        var returned = body.Data?.Interactions ?? [];
        checklist.RequireEqual("status = success", ChatTurnStatus.Success, body.Status);
        checklist.RequireEqual("gateway nhận đúng customerId", customerId, harness.CrmGateway.LastInteractionsCustomerId);
        checklist.RequireEqual("gateway nhận đúng limit", 5, harness.CrmGateway.LastInteractionsLimit);
        checklist.Require(
            "mọi interaction thuộc đúng customer",
            returned.All(interaction => interaction.CustomerId == customerId),
            $"customerIds=[{string.Join(", ", returned.Select(interaction => interaction.CustomerId).Distinct())}]");
        checklist.RequireSequenceEqual(
            "thứ tự newest-first khớp dataset", expectedIds, returned.Select(interaction => interaction.Id));
        checklist.Require(
            "occurredAtUtc giảm dần",
            returned.Zip(returned.Skip(1)).All(pair => pair.First.OccurredAtUtc >= pair.Second.OccurredAtUtc),
            string.Join(" > ", returned.Select(interaction => interaction.OccurredAtUtc.ToString("O"))));
        checklist.RequireSequenceEqual(
            "sourceIds khớp interactions", ScenarioDatasetSeed.CanonicalInteractionSourceIdsNewestFirst, body.SourceIds);

        var newest = returned.FirstOrDefault();
        checklist.RequireEqual("interaction mới nhất là interaction tiết kiệm canonical", savings.Id, newest?.Id);
        checklist.RequireEqual("interaction tiết kiệm có type = Call", "Call", newest?.Type);
        checklist.RequireEqual("interaction tiết kiệm có outcome = FollowUpRequired", "FollowUpRequired", newest?.Outcome);
        checklist.Require(
            "summary nêu nhu cầu tiền gửi 6 tháng",
            newest is not null
            && newest.Summary.Contains("tiền gửi", StringComparison.OrdinalIgnoreCase)
            && newest.Summary.Contains("6 tháng", StringComparison.OrdinalIgnoreCase),
            $"summary={newest?.Summary ?? "<null>"}");

        // limit is genuinely applied, not merely passed through.
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_interactions", new Dictionary<string, object> { ["customerId"] = customerId, ["limit"] = 2 }));
        var (_, limited) = await PostChatAsync(
            client, "Xem 2 tương tác gần nhất của CUS-0001.", Guid.NewGuid().ToString(), cancellationToken);
        var limitedIds = (limited.Data?.Interactions ?? []).Select(interaction => interaction.Id).ToList();
        checklist.RequireEqual("gateway nhận limit = 2", 2, harness.CrmGateway.LastInteractionsLimit);
        checklist.RequireSequenceEqual("limit=2 trả đúng 2 interaction mới nhất", expectedIds.Take(2), limitedIds);

        stopwatch.Stop();
        return Build(ScenarioId.T05, checklist, stopwatch);
    }

    // ------------------------------------------------------------------
    // T06 — Follow-up "khách hàng này" resolves from session state; sessions isolated; reset works.
    // ------------------------------------------------------------------
    private static async Task<ScenarioResult> RunT06Async(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checklist = new ScenarioChecklist();

        const string followUpMessage = "Xem các tương tác gần đây của khách hàng này.";
        var customerId = ScenarioDatasetSeed.CanonicalCustomerId;
        var sessionId = Guid.NewGuid().ToString();

        await using var harness = await AcceptanceHarness.CreateWithWebAsync(cancellationToken);
        var client = harness.CreateWebClient();

        // Turn 1 establishes the active customer.
        harness.ChatClient!.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_customer", new Dictionary<string, object> { ["customerId"] = customerId }));
        var (_, first) = await PostChatAsync(client, "Tìm hồ sơ khách hàng CUS-0001.", sessionId, cancellationToken);
        checklist.RequireEqual("lượt 1 thành công", ChatTurnStatus.Success, first.Status);

        // Turn 2: the message never names the customer, and the model's call omits customerId — so a
        // correct customerId downstream can only have come from conversation state.
        harness.ChatClient.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
            "get_interactions", new Dictionary<string, object> { ["limit"] = 5 }));
        var (_, second) = await PostChatAsync(client, followUpMessage, sessionId, cancellationToken);

        checklist.Require(
            "message lượt 2 không chứa mã khách hàng",
            !CustomerIdPattern.IsMatch(followUpMessage),
            followUpMessage);
        checklist.RequireEqual("lượt 2 thành công", ChatTurnStatus.Success, second.Status);
        checklist.RequireEqual(
            "customerId được resolve từ state", customerId, harness.CrmGateway.LastInteractionsCustomerId);

        // A brand-new session must not inherit anything.
        var (isolatedResponse, isolated) = await PostChatAsync(
            client, followUpMessage, Guid.NewGuid().ToString(), cancellationToken);
        checklist.RequireEqual("session mới: HTTP 400", HttpStatusCode.BadRequest, isolatedResponse.StatusCode);
        checklist.RequireEqual(
            "session mới: CUSTOMER_ID_REQUIRED", ChatTurnErrorCode.CustomerIdRequired, isolated.Error?.Code);
        checklist.Require("session mới: toolTrace rỗng", isolated.ToolTrace.Count == 0, $"count={isolated.ToolTrace.Count}");

        // Reset clears the active customer for the original session.
        var deleteResponse = await client.DeleteAsync($"/api/chat/sessions/{sessionId}", cancellationToken);
        checklist.RequireEqual("DELETE session trả 204", HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var (afterResetResponse, afterReset) = await PostChatAsync(client, followUpMessage, sessionId, cancellationToken);
        checklist.RequireEqual("sau reset: HTTP 400", HttpStatusCode.BadRequest, afterResetResponse.StatusCode);
        checklist.RequireEqual(
            "sau reset: CUSTOMER_ID_REQUIRED", ChatTurnErrorCode.CustomerIdRequired, afterReset.Error?.Code);

        stopwatch.Stop();
        return Build(ScenarioId.T06, checklist, stopwatch);
    }

    // ------------------------------------------------------------------
    // T07 — Email draft: contract, grounding discipline and no fabrication (deterministic layer).
    // The real-model grounding claim belongs to the live layer — see LiveAcceptanceScenarioTests.
    // ------------------------------------------------------------------
    private static async Task<ScenarioResult> RunT07Async(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checklist = new ScenarioChecklist();

        await using var harness = await AcceptanceHarness.CreateMcpOnlyAsync(cancellationToken);
        harness.KnowledgeRetriever.ProductResult = KnowledgeSearchResult.Found([EmailFixtures.ProductMatch]);
        harness.KnowledgeRetriever.TemplateResult = KnowledgeSearchResult.Found([EmailFixtures.TemplateMatch]);

        // The model claims requiresHumanApproval=false on purpose: the tool must force it back to
        // true rather than trusting the model's own field (docs/07 §7).
        harness.EmailDraftGenerator.Results.Enqueue(EmailFixtures.RawDraftClaimingNoApprovalNeeded());

        var root = await CallToolAsync(
            harness, "generate_email",
            new Dictionary<string, object?>
            {
                ["customerId"] = ScenarioDatasetSeed.CanonicalCustomerId,
                ["objective"] = "Follow-up nhu cầu gửi tiết kiệm kỳ hạn 6 tháng",
                ["tone"] = "professional_warm",
            },
            cancellationToken);

        checklist.RequireEqual("status = success", McpToolStatus.Success, GetString(root, "status"));

        var draft = root.TryGetProperty("data", out var data) && data.TryGetProperty("draft", out var draftElement)
            ? draftElement
            : default;
        var hasDraft = draft.ValueKind == JsonValueKind.Object;
        var subject = hasDraft ? draft.GetProperty("subject").GetString() ?? string.Empty : string.Empty;
        var bodyText = hasDraft ? draft.GetProperty("body").GetString() ?? string.Empty : string.Empty;

        checklist.Require("subject không rỗng", !string.IsNullOrWhiteSpace(subject), $"length={subject.Length}");
        checklist.Require("body không rỗng", !string.IsNullOrWhiteSpace(bodyText), $"length={bodyText.Length}");
        checklist.Require(
            "requiresHumanApproval = true (server ép, model khai false)",
            hasDraft && draft.GetProperty("requiresHumanApproval").GetBoolean(),
            "model trả false; tool phải ghi đè thành true");

        var sourceIds = ReadStringArray(root, "sourceIds");
        var allowed = EmailFixtures.AllowedSourceIds();
        var outsiders = sourceIds.Where(id => !allowed.Contains(id)).ToList();
        checklist.Require(
            "sourceIds là tập con của evidence đã retrieve",
            outsiders.Count == 0,
            $"ngoài tập cho phép=[{string.Join(", ", outsiders)}]");
        checklist.Require(
            "có ít nhất một kb:product:*",
            sourceIds.Any(id => id.StartsWith("kb:product:", StringComparison.Ordinal)),
            $"sourceIds=[{string.Join(", ", sourceIds)}]");
        checklist.Require(
            "có ít nhất một kb:email-template:*",
            sourceIds.Any(id => id.StartsWith("kb:email-template:", StringComparison.Ordinal)),
            $"sourceIds=[{string.Join(", ", sourceIds)}]");
        checklist.RequireContains("cite đúng product canonical", sourceIds, ScenarioDatasetSeed.CanonicalProductSourceId);

        checklist.Require(
            "không còn placeholder {{...}} trong subject/body",
            !PlaceholderPattern.IsMatch(subject) && !PlaceholderPattern.IsMatch(bodyText),
            $"tokens=[{string.Join(", ", PlaceholderPattern.Matches(subject + "\n" + bodyText).Select(match => match.Value))}]");
        checklist.Require(
            "tên khách hàng được restore đúng ở local",
            bodyText.Contains(ScenarioDatasetSeed.CanonicalCustomer.FullName, StringComparison.Ordinal),
            $"kỳ vọng body chứa tên canonical của {ScenarioDatasetSeed.CanonicalCustomerId}");
        checklist.Require(
            "subject và body là tiếng Việt có dấu",
            VietnameseDiacriticPattern.IsMatch(subject) && VietnameseDiacriticPattern.IsMatch(bodyText),
            "output bị ASCII-hoá/mất dấu");
        checklist.Require(
            "không bịa lãi suất / số liệu ngoài evidence",
            !FabricatedRatePattern.IsMatch(subject) && !FabricatedRatePattern.IsMatch(bodyText),
            "corpus knowledge không chứa ký tự '%' nào, nên mọi con số lãi suất đều là bịa");
        checklist.RequireEqual(
            "suggestedProductCode khớp evidence",
            ScenarioDatasetSeed.CanonicalProductCode,
            hasDraft ? draft.GetProperty("suggestedProductCode").GetString() : null);

        stopwatch.Stop();
        return Build(ScenarioId.T07, checklist, stopwatch);
    }

    // ------------------------------------------------------------------
    // T08 — Safety and resilience.
    // ------------------------------------------------------------------
    private static async Task<ScenarioResult> RunT08Async(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var checklist = new ScenarioChecklist();

        // (a) The PII gate fires before Gemini is ever contacted.
        await using (var piiHarness = await AcceptanceHarness.CreateWithWebAsync(cancellationToken))
        {
            var (piiResponse, piiBody) = await PostChatAsync(
                piiHarness.CreateWebClient(),
                "Liên hệ khách hàng qua email khachhang@example.test giúp tôi.",
                Guid.NewGuid().ToString(),
                cancellationToken);

            checklist.RequireEqual("PII gate: HTTP 400", HttpStatusCode.BadRequest, piiResponse.StatusCode);
            checklist.RequireEqual("PII gate: PII_REJECTED", ChatTurnErrorCode.PiiRejected, piiBody.Error?.Code);
            checklist.RequireEqual("PII gate: Gemini chưa từng được gọi", 0, piiHarness.ChatClient!.CallCount);
            checklist.Require("PII gate: toolTrace rỗng", piiBody.ToolTrace.Count == 0, $"count={piiBody.ToolTrace.Count}");
        }

        // (b) On a successful CRM turn, no raw customer PII is ever placed in Gemini's context.
        await using (var successHarness = await AcceptanceHarness.CreateWithWebAsync(cancellationToken))
        {
            successHarness.ChatClient!.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
                "get_customer", new Dictionary<string, object> { ["customerId"] = ScenarioDatasetSeed.CanonicalCustomerId }));
            var (_, ok) = await PostChatAsync(
                successHarness.CreateWebClient(), "Tìm hồ sơ khách hàng CUS-0001.", Guid.NewGuid().ToString(), cancellationToken);

            checklist.RequireEqual("PII-to-Gemini: lượt nền thành công", ChatTurnStatus.Success, ok.Status);

            var everythingSentToGemini = JsonSerializer.Serialize(
                successHarness.ChatClient.CapturedContents.Select(contents => contents.Select(content => content.Parts)));
            var leakedCount = ScenarioDatasetSeed.CanonicalPiiValues
                .Count(pii => everythingSentToGemini.Contains(pii, StringComparison.Ordinal));
            checklist.Require(
                "không giá trị PII thô nào được gửi tới Gemini",
                leakedCount == 0,
                $"số giá trị PII bị rò={leakedCount}/{ScenarioDatasetSeed.CanonicalPiiValues.Count}");
        }

        // (c) An unreachable MCP server degrades to a controlled, retryable error — no stack trace.
        await using (var brokenHarness = await AcceptanceHarness.CreateWithUnreachableMcpAsync(
            new HttpRequestException("simulated MCP connect failure"), cancellationToken))
        {
            brokenHarness.ChatClient!.Enqueue(FakeGeminiChatClient.FunctionCallResponse(
                "get_customer", new Dictionary<string, object> { ["customerId"] = ScenarioDatasetSeed.CanonicalCustomerId }));
            var (brokenResponse, broken) = await PostChatAsync(
                brokenHarness.CreateWebClient(), "Tìm hồ sơ khách hàng CUS-0001.", Guid.NewGuid().ToString(), cancellationToken);

            checklist.RequireEqual("MCP down: HTTP 503", HttpStatusCode.ServiceUnavailable, brokenResponse.StatusCode);
            checklist.RequireEqual("MCP down: MCP_UNAVAILABLE", ChatTurnErrorCode.McpUnavailable, broken.Error?.Code);
            checklist.RequireEqual("MCP down: retryable = true", true, broken.Error?.Retryable);

            var surfaced = $"{broken.Reply} {broken.Error?.Message}";
            checklist.Require(
                "MCP down: không lộ stack trace / exception thô",
                !StackTraceMarkers.Any(marker => surfaced.Contains(marker, StringComparison.Ordinal))
                && !surfaced.Contains("simulated MCP connect failure", StringComparison.Ordinal),
                $"surfaced={surfaced}");
        }

        // (d) AC-S05: retrieved text is data, not instructions — an injected command in a knowledge
        // document changes neither the approval flag nor the cited sources.
        await using (var injectionHarness = await AcceptanceHarness.CreateMcpOnlyAsync(cancellationToken))
        {
            injectionHarness.KnowledgeRetriever.ProductResult = KnowledgeSearchResult.Found([EmailFixtures.InjectedProductMatch]);
            injectionHarness.KnowledgeRetriever.TemplateResult = KnowledgeSearchResult.Found([EmailFixtures.TemplateMatch]);
            injectionHarness.EmailDraftGenerator.Results.Enqueue(EmailFixtures.RawDraftClaimingNoApprovalNeeded());

            var root = await CallToolAsync(
                injectionHarness, "generate_email",
                new Dictionary<string, object?>
                {
                    ["customerId"] = ScenarioDatasetSeed.CanonicalCustomerId,
                    ["objective"] = "Follow-up nhu cầu gửi tiết kiệm kỳ hạn 6 tháng",
                },
                cancellationToken);

            var draft = root.TryGetProperty("data", out var data) && data.TryGetProperty("draft", out var draftElement)
                ? draftElement
                : default;
            checklist.Require(
                "prompt injection: requiresHumanApproval vẫn true",
                draft.ValueKind == JsonValueKind.Object && draft.GetProperty("requiresHumanApproval").GetBoolean(),
                "một câu ra lệnh trong knowledge doc không được hạ cờ phê duyệt");

            var injectedSourceIds = ReadStringArray(root, "sourceIds");
            var allowed = EmailFixtures.AllowedSourceIds();
            checklist.Require(
                "prompt injection: không cite nguồn ngoài tập retrieve",
                injectedSourceIds.All(id => allowed.Contains(id)),
                $"sourceIds=[{string.Join(", ", injectedSourceIds)}]");
        }

        stopwatch.Stop();
        return Build(ScenarioId.T08, checklist, stopwatch);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------
    private static ScenarioResult Build(ScenarioId id, ScenarioChecklist checklist, Stopwatch stopwatch)
    {
        var (title, boundary) = Describe(id);
        return ScenarioResult.From(id, title, boundary, EvidenceClass.Deterministic, checklist, stopwatch.ElapsedMilliseconds);
    }

    private static async Task<(HttpResponseMessage Response, ChatResponse Body)> PostChatAsync(
        HttpClient client, string message, string sessionId, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/chat", new ChatRequest(message, sessionId), JsonOptions, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken);
        return (response, body!);
    }

    private static async Task<JsonElement> CallToolAsync(
        AcceptanceHarness harness, string toolName, IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var result = await harness.McpClient.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        var text = ((TextContentBlock)result.Content.Single()).Text;
        return JsonDocument.Parse(text).RootElement;
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) ? property.GetString() : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList()
            : [];

    /// <summary>
    /// Exception type + message only — never the stack trace, and never text that could carry
    /// payload data into the report.
    /// </summary>
    private static string Sanitize(Exception exception) =>
        $"không đánh giá được: {exception.GetType().Name}: {exception.Message}";
}
