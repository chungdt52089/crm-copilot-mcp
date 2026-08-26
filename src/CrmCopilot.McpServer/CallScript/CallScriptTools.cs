using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.Contracts.Pii;
using CrmCopilot.McpServer.Email;
using CrmCopilot.McpServer.Tools;
using ModelContextProtocol.Server;

namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// P0-10 MCP tool: generate_call_script (docs/07 §10 P1 tool, pulled forward).
///
/// Hybrid by design (plan D4): one MCP call performs the CRM lookups, selects a single opportunity,
/// masks PII, retrieves call-script and product evidence, and generates a fresh personalised draft.
/// The knowledge-base call scripts are playbooks used as grounding — never returned verbatim
/// (plan D5). Nothing here writes, sends, or places a call, so ReadOnly=true matches the same
/// environmental-side-effect profile as the other six tools.
///
/// Doing the retrieval inside the tool is what keeps the Host tool budget at 3 calls per turn: the
/// caller spends one call, not four.
/// </summary>
[McpServerToolType]
internal sealed class CallScriptTools(
    ICrmGateway crmGateway,
    IKnowledgeRetriever knowledgeRetriever,
    ICallScriptGenerator callScriptGenerator,
    ICallScriptTemplateCatalog templateCatalog,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CallScriptTools> logger)
{
    /// <summary>
    /// Deliberately the gateway maximum rather than a page size: the tool must see every
    /// opportunity to resolve an explicitly requested opportunityId and to pick the deterministic
    /// first Open one. Only ONE of them ever reaches the model (plan Amendment A2).
    /// </summary>
    private const int OpportunityLookupLimit = 20;

    private const int MinLetterCountForAccentCheck = 120;
    private const double MinVietnameseAccentRatio = 0.05;
    private const string CustomerNamePlaceholder = "{{CUSTOMER_NAME}}";
    private const string NotFoundMessage = "Không tìm thấy khách hàng phù hợp.";
    private const string OpportunityNotFoundMessage = "Không tìm thấy cơ hội bán phù hợp cho khách hàng này.";
    private const string ModelErrorMessage = "Không thể tạo kịch bản gọi từ Gemini.";
    private const string InternalErrorMessage = "Đã xảy ra lỗi không mong muốn.";

    private static readonly Regex OpportunityIdPattern = new(@"^OPP-\d{4}$", RegexOptions.Compiled);
    private static readonly Regex PercentagePattern = new(@"\d+(?:[.,]\d+)?\s*%", RegexOptions.Compiled);
    private static readonly Regex VndAmountPattern =
        new(@"\d[\d.,]*\s*(?:đồng|vnđ|vnd)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [McpServerTool(Name = "generate_call_script", ReadOnly = true, Destructive = false)]
    [Description("Tạo bản nháp kịch bản gọi điện tiếng Việt cho một khách hàng đã xác định. Tool này TỰ lấy interaction, cơ hội bán và TỰ retrieve playbook kịch bản cùng product knowledge. KHÔNG cần gọi get_interactions, get_opportunities hay search_product_knowledge trước. Nếu không nêu objective, tool tự suy ra mục tiêu từ cơ hội bán đang mở. Không thực hiện cuộc gọi. Luôn yêu cầu RM duyệt.")]
    public async Task<string> GenerateCallScript(
        [Description("Customer ID đã xác định, ví dụ CUS-0001.")] string customerId,
        [Description("Mục tiêu cuộc gọi (tiếng Việt), tối đa 500 ký tự. Bỏ trống để tool tự suy ra từ cơ hội bán.")] string? objective = null,
        [Description("Mã cơ hội bán cụ thể, ví dụ OPP-0002 (tuỳ chọn). Phải thuộc đúng khách hàng này.")] string? opportunityId = null,
        [Description("Mã sản phẩm cụ thể muốn đề cập (tuỳ chọn). Nếu cung cấp, phải khớp với evidence đã retrieve.")] string? productCode = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        string result;
        string status;
        string? errorCode = null;
        var modelWarningCount = 0;
        string? selectedOpportunityId = null;
        string objectiveSource = CallScriptObjectiveSources.Request;
        IReadOnlyList<string> finalSourceIds = [];
        IReadOnlyList<string> maskedFieldTypes = [];

        try
        {
            var validationError = Validate(customerId, objective, opportunityId, productCode);
            if (validationError is not null)
            {
                status = McpToolStatus.Error;
                errorCode = McpToolErrorCode.InvalidArgument;
                result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, validationError, retryable: false);
            }
            else
            {
                var lookup = await crmGateway.FindCustomerAsync(CustomerLookupQuery.ById(customerId), cancellationToken).ConfigureAwait(false);

                if (lookup.Status == CustomerLookupStatus.NotFound)
                {
                    status = McpToolStatus.NotFound;
                    errorCode = McpToolErrorCode.NotFound;
                    result = McpToolResponses.NotFound(traceId, McpToolErrorCode.NotFound, NotFoundMessage);
                }
                else if (lookup.Status == CustomerLookupStatus.Ambiguous)
                {
                    // Defensive-only, confirmed unreachable: CustomerLookupQuery.ById is an exact-id
                    // lookup (GET /api/customers/{id}), which only ever answers 200/404 — never 409.
                    status = McpToolStatus.Error;
                    errorCode = McpToolErrorCode.InternalError;
                    result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);
                }
                else
                {
                    (status, result, errorCode, modelWarningCount, selectedOpportunityId, objectiveSource, finalSourceIds, maskedFieldTypes) =
                        await GenerateForCustomerAsync(
                            traceId, lookup.Customer!, objective, opportunityId, productCode, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // expected outcome (client disconnect/timeout) — never wrapped as INTERNAL_ERROR
        }
        catch (KnowledgeGatewayException ex)
        {
            status = McpToolStatus.Error;
            errorCode = McpToolErrorCode.RagUnavailable;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.RagUnavailable, "Không thể truy xuất knowledge base.", ex.Retryable);
        }
        catch (CrmNotFoundException)
        {
            status = McpToolStatus.NotFound;
            errorCode = McpToolErrorCode.NotFound;
            result = McpToolResponses.NotFound(traceId, McpToolErrorCode.NotFound, NotFoundMessage);
        }
        catch (CrmUpstreamException ex)
        {
            status = McpToolStatus.Error;
            errorCode = McpToolErrorCode.UpstreamUnavailable;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.UpstreamUnavailable, "Không thể kết nối tới hệ thống CRM.", ex.Retryable);
        }
        catch (CallScriptGenerationException ex)
        {
            status = McpToolStatus.Error;
            errorCode = McpToolErrorCode.ModelError;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.ModelError, ModelErrorMessage, ex.Retryable);
        }
        catch (ArgumentException)
        {
            // Defensive fallback: Validate() above should make this unreachable.
            status = McpToolStatus.Error;
            errorCode = McpToolErrorCode.InvalidArgument;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "Tham số không hợp lệ.", retryable: false);
        }
        catch (Exception ex)
        {
            status = McpToolStatus.Error;
            errorCode = McpToolErrorCode.InternalError;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);

            // Never pass ex itself to the logger: most providers serialize ex.ToString() (full stack
            // trace + entire InnerException chain), and this project's exception convention puts the
            // raw SDK exception in InnerException — exactly what must never reach a log.
            logger.LogError(
                "MCP tool {ToolName} traceId={TraceId} exceptionType={ExceptionType} errorCode={ErrorCode} durationMs={DurationMs}",
                "generate_call_script", traceId, ex.GetType().Name, errorCode, stopwatch.ElapsedMilliseconds);
        }

        stopwatch.Stop();

        // Audit event: derived-safe fields only. No script body, customer name, email, phone,
        // account reference, opportunity amount, API key, model text, or SDK exception.
        logger.LogInformation(
            "MCP tool {ToolName} traceId={TraceId} status={Status} durationMs={DurationMs} " +
            "operation={Operation} customerIdHash={CustomerIdHash} selectedOpportunityId={SelectedOpportunityId} " +
            "objectiveSource={ObjectiveSource} retrievedSourceIds={RetrievedSourceIds} model={Model} " +
            "embeddingModel={EmbeddingModel} maskedFieldTypes={MaskedFieldTypes} warningCount={WarningCount} errorCode={ErrorCode}",
            "generate_call_script", traceId, status, stopwatch.ElapsedMilliseconds,
            "generate_call_script", HashForAudit(customerId), selectedOpportunityId ?? "(none)",
            objectiveSource, string.Join(",", finalSourceIds),
            CallScriptGenerationOptions.ModelId, Knowledge.GeminiEmbeddingOptions.ModelId,
            string.Join(",", maskedFieldTypes), modelWarningCount, errorCode ?? "(none)");

        return result;
    }

    private async Task<(string Status, string Result, string? ErrorCode, int ModelWarningCount, string? SelectedOpportunityId,
        string ObjectiveSource, IReadOnlyList<string> FinalSourceIds, IReadOnlyList<string> MaskedFieldTypes)>
        GenerateForCustomerAsync(
            string traceId, CustomerDto customer, string? objective, string? opportunityId, string? productCode,
            CancellationToken cancellationToken)
    {
        var interactions = await crmGateway
            .GetInteractionsAsync(customer.Id, CallScriptGenerationOptions.InteractionLimit, cancellationToken).ConfigureAwait(false);
        var opportunities = await crmGateway
            .GetOpportunitiesAsync(customer.Id, null, OpportunityLookupLimit, cancellationToken).ConfigureAwait(false);

        // Amendment A2 step 1: an explicitly requested opportunity that does not exist for THIS
        // customer is a hard not-found, decided before any model call is made.
        if (opportunityId is { Length: > 0 } &&
            !opportunities.Any(candidate => string.Equals(candidate.Id, opportunityId, StringComparison.Ordinal)))
        {
            return (McpToolStatus.NotFound,
                McpToolResponses.NotFound(traceId, McpToolErrorCode.NotFound, OpportunityNotFoundMessage),
                McpToolErrorCode.NotFound, 0, null, CallScriptObjectiveSources.Request, [], []);
        }

        var selected = SelectOpportunity(opportunities, opportunityId, productCode);
        var warnings = new List<string>();
        var (resolvedObjective, objectiveSource) = ResolveObjective(objective, selected, warnings);

        var maskedContext = PiiMasker.Mask(customer, interactions, resolvedObjective);
        var opportunityEvidence = selected is null ? null : ToSafeEvidence(selected);

        var retrievalQueryText = BuildRetrievalQueryText(maskedContext, selected, productCode);
        var callScriptEvidence = await ResolveCallScriptEvidenceAsync(
            objectiveSource, retrievalQueryText, warnings, cancellationToken).ConfigureAwait(false);
        var productResult = await knowledgeRetriever.SearchAsync(
            new KnowledgeSearchQuery(retrievalQueryText, [KnowledgeDocumentType.Product], CallScriptGenerationOptions.ProductTopK),
            cancellationToken).ConfigureAwait(false);
        var productMatches = productResult.Status == KnowledgeSearchStatus.Found ? productResult.Matches : [];

        // Amendment A3/D15: an explicitly requested productCode must be backed by real product
        // evidence. A call-script playbook is guidance about HOW to talk, never a substitute for
        // WHAT the product actually is, so it must not be allowed to paper over a missing product.
        var requestedProductMissing = productCode is not null &&
            !productMatches.Any(match => string.Equals(match.Metadata.ProductCode, productCode, StringComparison.Ordinal));

        if (requestedProductMissing || (callScriptEvidence.Count == 0 && productMatches.Count == 0))
        {
            return (McpToolStatus.NotFound, McpToolResponses.RagNoEvidence(traceId), null, 0,
                selected?.Id, objectiveSource, [], maskedContext.MaskedFieldTypes);
        }

        var allowedSourceIds = BuildAllowedSourceIds(callScriptEvidence, productMatches, maskedContext.Interactions, opportunityEvidence);

        var (insufficientEvidence, validRaw) = await GenerateAndValidateAsync(
            customer, maskedContext, resolvedObjective, opportunityEvidence, callScriptEvidence, productMatches,
            productCode, allowedSourceIds, cancellationToken).ConfigureAwait(false);

        if (insufficientEvidence)
        {
            return (McpToolStatus.NotFound, McpToolResponses.RagNoEvidence(traceId), null, 0,
                selected?.Id, objectiveSource, [], maskedContext.MaskedFieldTypes);
        }

        if (validRaw is null)
        {
            return (McpToolStatus.Error,
                McpToolResponses.Error(traceId, McpToolErrorCode.ModelError, ModelErrorMessage, retryable: true),
                McpToolErrorCode.ModelError, 0, selected?.Id, objectiveSource, [], maskedContext.MaskedFieldTypes);
        }

        var draft = BuildDraft(
            validRaw, customer, maskedContext, resolvedObjective, objectiveSource, warnings,
            opportunityEvidence, opportunityId is { Length: > 0 }, productMatches, allowedSourceIds);

        return (McpToolStatus.Success,
            McpToolResponses.Success(traceId, draft.SourceIds, new GenerateCallScriptData(draft)),
            null, validRaw.Warnings!.Count, draft.SelectedOpportunityId, objectiveSource,
            draft.SourceIds, maskedContext.MaskedFieldTypes);
    }

    /// <summary>
    /// Amendment A2. Deterministic and short-circuiting: the gateway already returns opportunities
    /// ordered ExpectedCloseDateUtc ascending then Id ascending, so "the first Open one" is a
    /// specific record rather than whichever happened to enumerate first.
    /// </summary>
    private static OpportunityDto? SelectOpportunity(
        IReadOnlyList<OpportunityDto> opportunities, string? opportunityId, string? productCode)
    {
        if (opportunityId is { Length: > 0 })
        {
            return opportunities.FirstOrDefault(candidate => string.Equals(candidate.Id, opportunityId, StringComparison.Ordinal));
        }

        if (productCode is { Length: > 0 })
        {
            var byProduct = opportunities.FirstOrDefault(candidate =>
                string.Equals(candidate.Status, OpportunityStatuses.Open, StringComparison.Ordinal) &&
                string.Equals(candidate.ProductCode, productCode, StringComparison.Ordinal));

            if (byProduct is not null)
            {
                return byProduct;
            }
        }

        return opportunities.FirstOrDefault(candidate =>
            string.Equals(candidate.Status, OpportunityStatuses.Open, StringComparison.Ordinal));
    }

    /// <summary>
    /// Amendment A6. Derives an objective when the caller supplied none, so a bare
    /// "Soạn kịch bản gọi cho khách hàng CUS-0001" is answerable deterministically instead of being
    /// rejected. The derived text is built from the opportunity productCode and stage — both known
    /// without retrieval — which also makes it a good retrieval query.
    /// </summary>
    private static (string ResolvedObjective, string ObjectiveSource) ResolveObjective(
        string? objective, OpportunityDto? selected, List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(objective))
        {
            return (objective.Trim(), CallScriptObjectiveSources.Request);
        }

        warnings.Add(CallScriptWarnings.ObjectiveInferred);

        if (selected is not null)
        {
            return ($"Trao đổi với khách hàng về cơ hội sản phẩm {selected.ProductCode} đang ở giai đoạn {selected.Stage}.",
                CallScriptObjectiveSources.Opportunity);
        }

        return (CallScriptGenerationOptions.CustomerFollowUpObjective, CallScriptObjectiveSources.CustomerFollowUp);
    }

    /// <summary>
    /// Amendment A6 step 4. The periodic-care path pins its playbook by id so the short-sentence
    /// demo is reproducible; every other path uses real semantic retrieval. If the pinned script is
    /// somehow absent from the dataset this falls back to retrieval and does NOT claim
    /// TEMPLATE_PINNED — the warning must describe what actually happened.
    /// </summary>
    private async Task<IReadOnlyList<CallScriptEvidence>> ResolveCallScriptEvidenceAsync(
        string objectiveSource, string retrievalQueryText, List<string> warnings, CancellationToken cancellationToken)
    {
        if (objectiveSource == CallScriptObjectiveSources.CustomerFollowUp &&
            templateCatalog.FindByScriptId(CallScriptGenerationOptions.PeriodicCareScriptId) is { } pinned)
        {
            warnings.Add(CallScriptWarnings.TemplatePinned);
            return [pinned];
        }

        var searchResult = await knowledgeRetriever.SearchAsync(
            new KnowledgeSearchQuery(retrievalQueryText, [KnowledgeDocumentType.CallScript], CallScriptGenerationOptions.CallScriptTopK),
            cancellationToken).ConfigureAwait(false);

        return searchResult.Status == KnowledgeSearchStatus.Found
            ? [.. searchResult.Matches.Select(CallScriptEvidence.FromMatch)]
            : [];
    }

    private async Task<(bool InsufficientEvidence, RawCallScriptModel? ValidRaw)> GenerateAndValidateAsync(
        CustomerDto customer,
        MaskedEmailContext maskedContext,
        string resolvedObjective,
        SafeOpportunityEvidence? opportunityEvidence,
        IReadOnlyList<CallScriptEvidence> callScriptEvidence,
        IReadOnlyList<KnowledgeMatch> productMatches,
        string? requestedProductCode,
        IReadOnlyList<string> allowedSourceIds,
        CancellationToken cancellationToken)
    {
        string? correctiveInstruction = null;

        for (var attempt = 1; attempt <= CallScriptGenerationOptions.MaxAttempts; attempt++)
        {
            var promptContext = new CallScriptPromptContext(
                MaskObjectiveForPrompt(maskedContext, resolvedObjective), customer.Segment, maskedContext.Interactions,
                opportunityEvidence, callScriptEvidence, productMatches, requestedProductCode, correctiveInstruction,
                ResolveLanguage(customer));

            var raw = await callScriptGenerator.GenerateAsync(promptContext, cancellationToken).ConfigureAwait(false);

            if (raw is { Status: RawCallScriptModel.StatusInsufficientEvidence })
            {
                // Terminal on either attempt — a model correctly reporting insufficient evidence is
                // not a defect and is never retried (plan Amendment A8 mapping table).
                return (true, null);
            }

            if (raw is { Status: RawCallScriptModel.StatusOk })
            {
                var failureReason = ValidateOkDraft(raw, allowedSourceIds, callScriptEvidence, productMatches, requestedProductCode, customer);
                if (failureReason is null)
                {
                    return (false, raw);
                }

                correctiveInstruction = CorrectiveInstructionFor(failureReason);
                continue;
            }

            // raw is null (model text failed to deserialize) or Status is neither ok nor
            // insufficient_evidence.
            correctiveInstruction = CorrectiveInstructionFor("invalid_json");
        }

        return (false, null);
    }

    private static string? Validate(string customerId, string? objective, string? opportunityId, string? productCode)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return "customerId là bắt buộc.";
        }

        // objective is optional (Amendment A5) — but a supplied one still has to be usable.
        if (objective is not null)
        {
            if (string.IsNullOrWhiteSpace(objective))
            {
                return "objective rỗng.";
            }

            if (objective.Length > CallScriptGenerationOptions.MaxObjectiveLength)
            {
                return $"objective vượt quá {CallScriptGenerationOptions.MaxObjectiveLength} ký tự.";
            }
        }

        if (opportunityId is not null)
        {
            if (string.IsNullOrWhiteSpace(opportunityId))
            {
                return "opportunityId rỗng.";
            }

            if (opportunityId.Length > CallScriptGenerationOptions.MaxOpportunityIdLength ||
                !OpportunityIdPattern.IsMatch(opportunityId))
            {
                return "opportunityId không đúng định dạng (ví dụ OPP-0002).";
            }
        }

        if (productCode is not null)
        {
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return "productCode rỗng.";
            }

            if (productCode.Length > ProductCodeFormat.MaxLength)
            {
                return $"productCode vượt quá {ProductCodeFormat.MaxLength} ký tự.";
            }

            if (!ProductCodeFormat.IsWellFormed(productCode))
            {
                return "productCode không đúng định dạng (ví dụ PRD-SAV-006M).";
            }

            // Defense-in-depth, expected-unreachable given the format check above.
            if (PiiPatterns.Email.IsMatch(productCode) || PiiPatterns.Phone.IsMatch(productCode) ||
                PiiPatterns.DigitRun.IsMatch(productCode) || PiiPatterns.SecretToken.IsMatch(productCode))
            {
                return "productCode chứa dữ liệu không hợp lệ.";
            }
        }

        return null;
    }

    /// <summary>
    /// PiiMasker.Mask already masked the resolved objective as its free-text argument; this returns
    /// that masked form rather than the raw string, so the raw objective never reaches the prompt.
    /// </summary>
    private static string MaskObjectiveForPrompt(MaskedEmailContext maskedContext, string resolvedObjective) =>
        string.IsNullOrWhiteSpace(maskedContext.MaskedObjective) ? resolvedObjective : maskedContext.MaskedObjective;

    private static SafeOpportunityEvidence ToSafeEvidence(OpportunityDto opportunity) => new(
        $"crm:opportunity:{opportunity.Id}",
        opportunity.ProductCode,
        opportunity.Stage,
        opportunity.Status,
        opportunity.ExpectedCloseDateUtc,
        ToAmountBand(opportunity.AmountVnd));

    /// <summary>Coarse buckets, deliberately not the exact figure (plan D12).</summary>
    internal static string ToAmountBand(long amountVnd) => amountVnd switch
    {
        < 100_000_000 => "<100 triệu",
        < 500_000_000 => "100-500 triệu",
        < 1_000_000_000 => "500 triệu-1 tỷ",
        _ => ">1 tỷ",
    };

    private static string BuildRetrievalQueryText(MaskedEmailContext maskedContext, OpportunityDto? selected, string? productCode)
    {
        var text = maskedContext.MaskedObjective + "\n" + string.Join("\n", maskedContext.RetrievalQuerySummaries);

        if (selected is not null)
        {
            text += "\n" + selected.ProductCode;
        }

        return productCode is not null ? text + "\n" + productCode : text;
    }

    private static IReadOnlyList<string> BuildAllowedSourceIds(
        IReadOnlyList<CallScriptEvidence> callScriptEvidence,
        IReadOnlyList<KnowledgeMatch> productMatches,
        IReadOnlyList<InteractionEvidence> interactions,
        SafeOpportunityEvidence? opportunityEvidence)
    {
        List<string> allowed =
        [
            .. callScriptEvidence.Select(evidence => evidence.SourceId),
            .. productMatches.Select(match => match.SourceId),
            .. interactions.Select(evidence => evidence.SourceId),
        ];

        if (opportunityEvidence is not null)
        {
            allowed.Add(opportunityEvidence.SourceId);
        }

        return allowed;
    }

    /// <summary>Ordered checks; first failure short-circuits with a reason string consumed by
    /// <see cref="CorrectiveInstructionFor"/>. Runs on the model raw, literal output — strictly
    /// before any placeholder restore — so a model that wrote the real customer name directly
    /// (bypassing the placeholder) is caught here, not laundered by the later restore step.</summary>
    private static string? ValidateOkDraft(
        RawCallScriptModel raw,
        IReadOnlyList<string> allowedSourceIds,
        IReadOnlyList<CallScriptEvidence> callScriptEvidence,
        IReadOnlyList<KnowledgeMatch> productMatches,
        string? requestedProductCode,
        CustomerDto customer)
    {
        if (string.IsNullOrWhiteSpace(raw.Opening) || string.IsNullOrWhiteSpace(raw.Closing))
        {
            return "empty_opening_or_closing";
        }

        if (raw.DiscoveryQuestions is not { Count: > 0 } || raw.TalkingPoints is not { Count: > 0 })
        {
            return "empty_sections";
        }

        // System.Text.Json does not enforce non-null for reference-type properties, so a model
        // returning JSON null for a "required" array is a real, reachable case — not a formality.
        if (raw.ObjectionHandling is null || raw.Warnings is null)
        {
            return "invalid_json";
        }

        if (raw.ObjectionHandling.Any(item =>
                item is null || string.IsNullOrWhiteSpace(item.Objection) || string.IsNullOrWhiteSpace(item.Response)))
        {
            return "invalid_objection_handling";
        }

        if (raw.UsedSourceIds is null || raw.UsedSourceIds.Count == 0)
        {
            return "used_source_ids_empty";
        }

        if (!raw.UsedSourceIds.All(id => allowedSourceIds.Contains(id, StringComparer.Ordinal)))
        {
            return "source_ids_not_subset";
        }

        if (!raw.UsedSourceIds.Any(id =>
                id.StartsWith("kb:product:", StringComparison.Ordinal) ||
                id.StartsWith("kb:call-script:", StringComparison.Ordinal)))
        {
            return "used_source_ids_lack_knowledge_grounding";
        }

        if (raw.SuggestedProductCode is { Length: > 0 } suggested &&
            !productMatches.Any(match => string.Equals(match.Metadata.ProductCode, suggested, StringComparison.Ordinal)))
        {
            return "product_code_not_in_evidence";
        }

        if (requestedProductCode is not null)
        {
            if (!string.Equals(raw.SuggestedProductCode, requestedProductCode, StringComparison.Ordinal))
            {
                return "suggested_product_code_mismatch_requested";
            }

            // Amendment A3: when a specific product was requested, the model must actually cite it.
            var requestedSourceId = productMatches
                .FirstOrDefault(match => string.Equals(match.Metadata.ProductCode, requestedProductCode, StringComparison.Ordinal))
                ?.SourceId;

            if (requestedSourceId is null || !raw.UsedSourceIds.Contains(requestedSourceId, StringComparer.Ordinal))
            {
                return "requested_product_source_not_cited";
            }
        }

        var combinedText = CombineText(raw);

        if (PiiPatterns.Email.IsMatch(combinedText) || PiiPatterns.Phone.IsMatch(combinedText) ||
            PiiPatterns.DigitRun.IsMatch(combinedText) || PiiPatterns.SecretToken.IsMatch(combinedText) ||
            combinedText.Contains(customer.FullName, StringComparison.Ordinal) ||
            combinedText.Contains(customer.Email, StringComparison.Ordinal) ||
            combinedText.Contains(customer.Phone, StringComparison.Ordinal) ||
            combinedText.Contains(customer.AccountReference, StringComparison.Ordinal))
        {
            return "pii_detected_in_output";
        }

        if (!NumericClaimsAreVerified(combinedText, callScriptEvidence, productMatches))
        {
            return "unverified_numeric_claim";
        }

        if (string.Equals(ResolveLanguage(customer), CallScriptGenerationOptions.DefaultLanguage, StringComparison.OrdinalIgnoreCase) &&
            LacksVietnameseDiacritics(combinedText))
        {
            return "unaccented_vietnamese";
        }

        return null;
    }

    private static string CombineText(RawCallScriptModel raw) =>
        string.Join(
            "\n",
            [
                raw.Opening ?? string.Empty,
                .. raw.DiscoveryQuestions ?? [],
                .. raw.TalkingPoints ?? [],
                .. (raw.ObjectionHandling ?? []).SelectMany(item => new[] { item.Objection ?? string.Empty, item.Response ?? string.Empty }),
                raw.Closing ?? string.Empty,
            ]);

    private static string ResolveLanguage(CustomerDto customer) =>
        string.IsNullOrWhiteSpace(customer.PreferredLanguage)
            ? CallScriptGenerationOptions.DefaultLanguage
            : customer.PreferredLanguage;

    /// <summary>Same blunt check EmailTools uses: fires only when text long enough to judge contains
    /// essentially no Vietnamese-accented letters at all. Never adds diacritics itself.</summary>
    private static bool LacksVietnameseDiacritics(string text)
    {
        var prose = text.Replace(CustomerNamePlaceholder, string.Empty, StringComparison.Ordinal);

        var letterCount = 0;
        var accentedLetterCount = 0;

        foreach (var character in prose)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            letterCount++;
            if (character > sbyte.MaxValue)
            {
                accentedLetterCount++;
            }
        }

        return letterCount >= MinLetterCountForAccentCheck &&
               accentedLetterCount < letterCount * MinVietnameseAccentRatio;
    }

    private static bool NumericClaimsAreVerified(
        string text, IReadOnlyList<CallScriptEvidence> callScriptEvidence, IReadOnlyList<KnowledgeMatch> productMatches)
    {
        var evidenceText = string.Join(
            '\n',
            callScriptEvidence.Select(evidence => evidence.Content).Concat(productMatches.Select(match => match.Content)));

        foreach (Match match in PercentagePattern.Matches(text))
        {
            if (!evidenceText.Contains(match.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (Match match in VndAmountPattern.Matches(text))
        {
            if (!evidenceText.Contains(match.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string CorrectiveInstructionFor(string reason) => reason switch
    {
        "empty_opening_or_closing" =>
            "Phản hồi trước có opening hoặc closing rỗng dù status là \"ok\". Cung cấp đầy đủ, hoặc trả status \"insufficient_evidence\" nếu không đủ evidence.",
        "empty_sections" =>
            "Phản hồi trước thiếu discoveryQuestions hoặc talkingPoints. Mỗi mục phải có ít nhất một phần tử có nội dung.",
        "invalid_objection_handling" =>
            "Phản hồi trước có phần tử objectionHandling thiếu objection hoặc response. Mỗi phần tử phải có đủ cả hai và không được rỗng.",
        "used_source_ids_empty" =>
            "Phản hồi trước có usedSourceIds rỗng dù status là \"ok\". Phải trích dẫn ít nhất một source ID từ evidence đã cung cấp.",
        "source_ids_not_subset" =>
            "Phản hồi trước có usedSourceIds chứa giá trị không nằm trong evidence đã cung cấp. Chỉ được dùng đúng các source ID đã cấp.",
        "used_source_ids_lack_knowledge_grounding" =>
            "Phản hồi trước chỉ trích dẫn source ID không phải kiến thức sản phẩm/playbook. Phải có ít nhất một source ID dạng kb:product: hoặc kb:call-script:.",
        "product_code_not_in_evidence" =>
            "Phản hồi trước có suggestedProductCode không khớp evidence sản phẩm. Chỉ dùng productCode xuất hiện trong EVIDENCE_PRODUCT, hoặc để null.",
        "suggested_product_code_mismatch_requested" =>
            "REQUESTED_PRODUCT_CODE đã chỉ định một mã cụ thể. suggestedProductCode phải đúng bằng mã đó, không được đề xuất mã khác.",
        "requested_product_source_not_cited" =>
            "REQUESTED_PRODUCT_CODE đã chỉ định một mã cụ thể. usedSourceIds PHẢI chứa source ID của đúng sản phẩm đó trong EVIDENCE_PRODUCT.",
        "pii_detected_in_output" =>
            "Phản hồi trước chứa dữ liệu dạng email/số điện thoại/số tài khoản/chuỗi giống secret. Giữ nguyên placeholder {{CUSTOMER_NAME}}; không tự chèn thông tin liên hệ thật.",
        "unaccented_vietnamese" =>
            "Phản hồi trước viết tiếng Việt KHÔNG DẤU. Viết lại toàn bộ kịch bản bằng tiếng Việt có dấu đầy đủ, dùng đúng chữ Unicode tiếng Việt (ví dụ \"Kính chào\", không phải \"Kinh chao\"). Giữ nguyên placeholder {{CUSTOMER_NAME}}.",
        "unverified_numeric_claim" =>
            "Phản hồi trước có số liệu (%, số tiền) không xuất hiện trong evidence. Chỉ nêu số liệu có trong evidence; nếu evidence không có số liệu, không đề cập số liệu.",
        _ => // "invalid_json" and any other unrecognized reason
            "Phản hồi trước không phải JSON hợp lệ khớp schema đã cung cấp. Hãy trả về CHÍNH XÁC một object JSON khớp schema, không có markdown hay văn bản nào khác ngoài JSON.",
    };

    /// <summary>Only ever called with a <paramref name="raw"/> that already passed
    /// <see cref="ValidateOkDraft"/>, so the null-forgiving accesses below hold by that established
    /// invariant rather than by assumption.</summary>
    private static CallScriptDraftDto BuildDraft(
        RawCallScriptModel raw,
        CustomerDto customer,
        MaskedEmailContext maskedContext,
        string resolvedObjective,
        string objectiveSource,
        IReadOnlyList<string> warnings,
        SafeOpportunityEvidence? opportunityEvidence,
        bool opportunityWasExplicit,
        IReadOnlyList<KnowledgeMatch> productMatches,
        IReadOnlyList<string> allowedSourceIds)
    {
        // An AUTO-SELECTED opportunity the finished draft turns out not to be about is not evidence
        // for it, so it is dropped from the reported selection as well as from the sources. An
        // explicitly requested one is never dropped: that is the caller's stated intent, not a guess
        // this tool made, and silently discarding a supplied argument would be its own defect.
        var retainedOpportunity = opportunityEvidence is not null &&
            (opportunityWasExplicit || OpportunityIsCorroborated(raw, opportunityEvidence, productMatches))
                ? opportunityEvidence
                : null;

        return new CallScriptDraftDto(
            RestorePlaceholder(raw.Opening!.Trim(), customer.FullName, isOpening: true),
            [.. raw.DiscoveryQuestions!.Select(question => RestorePlaceholder(question, customer.FullName, isOpening: false))],
            [.. raw.TalkingPoints!.Select(point => RestorePlaceholder(point, customer.FullName, isOpening: false))],
            [.. raw.ObjectionHandling!.Select(item => new ObjectionHandlingItemDto(
                RestorePlaceholder(item.Objection!, customer.FullName, isOpening: false),
                RestorePlaceholder(item.Response!, customer.FullName, isOpening: false)))],
            RestorePlaceholder(raw.Closing!.Trim(), customer.FullName, isOpening: false),
            raw.SuggestedProductCode,
            retainedOpportunity is null ? null : ExtractOpportunityId(retainedOpportunity.SourceId),
            resolvedObjective,
            objectiveSource,
            BuildFinalSourceIds(raw, retainedOpportunity, allowedSourceIds),
            RequiresHumanApproval: true, // always server-forced — never trusts raw.RequiresHumanApproval
            warnings,
            new PiiMaskSummaryDto(maskedContext.MaskedFieldTypes));
    }

    /// <summary>
    /// Browser-verified P0-10 finding, and a correction to how Amendment A3 was first implemented.
    ///
    /// A3 asked the server — not the model — to own the source list, and the first implementation
    /// took that literally: every piece of evidence placed in the prompt was forced in. Retrieval
    /// hands the prompt three products and two playbooks, so a savings call script came back citing
    /// PRD-SAV-012M and PRD-LOAN-PERSONAL-01 as well, plus both playbooks. Those are retrieval
    /// CANDIDATES, not grounding: presenting them as sources overstates what the draft rests on,
    /// which is worse than under-citing because an RM reads the chips as provenance.
    ///
    /// So the model's own citations are the basis again — the same rule generate_email uses, and
    /// the reason its sources were already correct. This is not "trusting the model": ValidateOkDraft
    /// has already forced every cited id to be a subset of the allowed evidence, required at least
    /// one kb:product:/kb:call-script: citation, and required the requested product to be cited when
    /// one was specified. A draft therefore cannot reach here with an empty or invented source list.
    ///
    /// The selected opportunity is the one server-forced entry, and only when
    /// <see cref="OpportunityIsCorroborated"/> confirms the draft is actually about that
    /// opportunity's product — see that method for why an uncorroborated one is dropped entirely.
    /// </summary>
    private static IReadOnlyList<string> BuildFinalSourceIds(
        RawCallScriptModel raw, SafeOpportunityEvidence? retainedOpportunity, IReadOnlyList<string> allowedSourceIds)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string sourceId)
        {
            if (seen.Add(sourceId))
            {
                ordered.Add(sourceId);
            }
        }

        if (retainedOpportunity is not null)
        {
            Add(retainedOpportunity.SourceId);
        }

        foreach (var sourceId in raw.UsedSourceIds!.Where(id => allowedSourceIds.Contains(id, StringComparer.Ordinal)))
        {
            Add(sourceId);
        }

        return ordered;
    }

    /// <summary>
    /// Is the auto-selected opportunity actually what this draft is about?
    ///
    /// Selection (Amendment A2) runs BEFORE retrieval, because the objective may have to be derived
    /// from the opportunity. With no opportunityId and no productCode it simply takes the customer's
    /// first Open opportunity — which is the right default, but says nothing about whether that
    /// opportunity matches the objective the RM actually asked about. Asking for a loan call script
    /// would still attach the customer's open savings opportunity.
    ///
    /// The check is deliberately narrow and evidence-based: the opportunity is corroborated when the
    /// finished draft is about its product — either the draft suggests that product code, or it
    /// cited that product's knowledge source. An uncorroborated opportunity is dropped from both
    /// SelectedOpportunityId and sourceIds rather than reported as grounding it did not provide.
    ///
    /// On the objective-inferred path this is satisfied by construction: the derived objective names
    /// the opportunity's own product code, so retrieval surfaces that product and the draft uses it.
    /// </summary>
    private static bool OpportunityIsCorroborated(
        RawCallScriptModel raw, SafeOpportunityEvidence opportunity, IReadOnlyList<KnowledgeMatch> productMatches)
    {
        if (string.Equals(raw.SuggestedProductCode, opportunity.ProductCode, StringComparison.Ordinal))
        {
            return true;
        }

        var opportunityProductSourceId = productMatches
            .FirstOrDefault(match => string.Equals(match.Metadata.ProductCode, opportunity.ProductCode, StringComparison.Ordinal))
            ?.SourceId;

        return opportunityProductSourceId is not null &&
               raw.UsedSourceIds!.Contains(opportunityProductSourceId, StringComparer.Ordinal);
    }

    private static string ExtractOpportunityId(string sourceId) =>
        sourceId.StartsWith("crm:opportunity:", StringComparison.Ordinal)
            ? sourceId["crm:opportunity:".Length..]
            : sourceId;

    /// <summary>Restores the model placeholder with the real synthetic name. If the model dropped it
    /// the opening falls back to a neutral greeting prefix; other sections are returned unchanged
    /// (a greeting prefix only makes sense as an opening line).</summary>
    private static string RestorePlaceholder(string text, string customerFullName, bool isOpening)
    {
        if (text.Contains(CustomerNamePlaceholder, StringComparison.Ordinal))
        {
            return text.Replace(CustomerNamePlaceholder, customerFullName, StringComparison.Ordinal);
        }

        return isOpening ? "Kính chào Anh/Chị, " + text : text;
    }

    /// <summary>SHA-256 over the raw customerId, unsalted — customerId is a low-sensitivity
    /// synthetic identifier, not a secret; the hash exists only to avoid raw-id correlation in log
    /// lines. Accepts null because an MCP client can send a null/missing argument even though the
    /// parameter is declared non-nullable, and this runs unconditionally after the try/catch.</summary>
    private static string HashForAudit(string? value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))[..16];
}
