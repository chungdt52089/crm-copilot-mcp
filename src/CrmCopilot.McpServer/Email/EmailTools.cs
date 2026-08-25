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
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.McpServer.Tools;
using ModelContextProtocol.Server;

namespace CrmCopilot.McpServer.Email;

/// <summary>
/// P0-07 MCP tool: generate_email (docs/07_MCP_TOOL_CONTRACTS.md §7, docs/08_RAG_EMAIL_AND_PII_SPEC.md).
/// Fetches customer + recent interactions, masks PII (PiiMasker), retrieves product/template
/// evidence (existing IKnowledgeRetriever, reused as-is), generates a structured draft via Gemini
/// (IEmailDraftGenerator) with one allowed retry on validation failure, restores the
/// {{CUSTOMER_NAME}} placeholder, and always server-forces requiresHumanApproval=true. No CRM
/// write, no send, no persisted state — ReadOnly=true (same environmental-side-effect profile as
/// the other three P0 tools).
/// </summary>
[McpServerToolType]
internal sealed class EmailTools(
    ICrmGateway crmGateway,
    IKnowledgeRetriever knowledgeRetriever,
    IEmailDraftGenerator emailDraftGenerator,
    IHttpContextAccessor httpContextAccessor,
    ILogger<EmailTools> logger)
{
    private const int MaxObjectiveLength = 500;
    private const int MaxProductCodeLength = 40;
    private const int ProductTopK = 3;
    private const int TemplateTopK = 2;
    private const int InteractionLimit = 5;

    /// <summary>Minimum letters (placeholder excluded) before the diacritics check can fire at all —
    /// a short body has too little signal to judge. See <see cref="LacksVietnameseDiacritics"/>.</summary>
    private const int MinLetterCountForAccentCheck = 120;

    /// <summary>Genuine Vietnamese prose runs ~15-25% accented letters; 5% is an order of magnitude
    /// below that, so only near-total absence of diacritics trips it.</summary>
    private const double MinVietnameseAccentRatio = 0.05;
    private const string CustomerNamePlaceholder = "{{CUSTOMER_NAME}}";
    private const string NotFoundMessage = "Không tìm thấy khách hàng phù hợp.";
    private const string ModelErrorMessage = "Không thể tạo email draft từ Gemini.";
    private const string InternalErrorMessage = "Đã xảy ra lỗi không mong muốn.";

    private static readonly string[] AllowedTones = ["professional", "professional_warm", "concise"];

    // Derived from the 6 checked-in records in data/knowledge/products.json (docs/06 has no
    // formal regex of its own): PRD- followed by 2-3 further hyphen-separated uppercase-
    // alphanumeric segments, e.g. PRD-SAV-006M, PRD-CARD-CASHBACK-01.
    private static readonly Regex ProductCodePattern = new(@"^PRD-[A-Z0-9]+(-[A-Z0-9]+){1,3}$", RegexOptions.Compiled);

    private static readonly Regex PercentagePattern = new(@"\d+(?:[.,]\d+)?\s*%", RegexOptions.Compiled);
    private static readonly Regex VndAmountPattern =
        new(@"\d[\d.,]*\s*(?:đồng|vnđ|vnd)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [McpServerTool(Name = "generate_email", ReadOnly = true, Destructive = false)]
    [Description("Tạo bản nháp email tiếng Việt cho một khách hàng đã xác định. Tool này TỰ lấy các interaction gần nhất của khách hàng và TỰ retrieve product knowledge cùng email template cần thiết. KHÔNG cần gọi search_product_knowledge trước hoặc song song với tool này. Không gửi email. Luôn yêu cầu RM duyệt.")]
    public async Task<string> GenerateEmail(
        [Description("Customer ID đã xác định, ví dụ CUS-0001.")] string customerId,
        [Description("Mục tiêu của email (tiếng Việt), tối đa 500 ký tự.")] string objective,
        [Description("Tone: professional, professional_warm, hoặc concise. Mặc định professional_warm.")] string tone = "professional_warm",
        [Description("Mã sản phẩm cụ thể muốn đề cập (tuỳ chọn). Nếu cung cấp, vẫn phải khớp với evidence đã retrieve.")] string? productCode = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        string result;
        string status;
        string? errorCode = null;
        var warningCount = 0;
        IReadOnlyList<string> allowedSourceIds = [];
        IReadOnlyList<string> maskedFieldTypes = [];

        try
        {
            var validationError = Validate(customerId, objective, tone, productCode);
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
                    var customer = lookup.Customer!;
                    var interactions = await crmGateway.GetInteractionsAsync(customerId, InteractionLimit, cancellationToken).ConfigureAwait(false);

                    var maskedContext = PiiMasker.Mask(customer, interactions, objective);
                    maskedFieldTypes = maskedContext.MaskedFieldTypes;

                    var retrievalQueryText = BuildRetrievalQueryText(maskedContext, productCode);
                    var productResult = await knowledgeRetriever.SearchAsync(
                        new KnowledgeSearchQuery(retrievalQueryText, [KnowledgeDocumentType.Product], ProductTopK), cancellationToken).ConfigureAwait(false);
                    var templateResult = await knowledgeRetriever.SearchAsync(
                        new KnowledgeSearchQuery(retrievalQueryText, [KnowledgeDocumentType.EmailTemplate], TemplateTopK), cancellationToken).ConfigureAwait(false);

                    var productMatches = productResult.Status == KnowledgeSearchStatus.Found ? productResult.Matches : [];
                    var templateMatches = templateResult.Status == KnowledgeSearchStatus.Found ? templateResult.Matches : [];

                    var requestedProductMissing = productCode is not null &&
                        !productMatches.Any(match => match.Metadata.ProductCode == productCode);

                    if ((productMatches.Count == 0 && templateMatches.Count == 0) || requestedProductMissing)
                    {
                        status = McpToolStatus.NotFound;
                        result = McpToolResponses.RagNoEvidence(traceId);
                    }
                    else
                    {
                        allowedSourceIds = BuildAllowedSourceIds(productMatches, templateMatches, maskedContext.Interactions);

                        var (insufficientEvidence, validRaw) = await GenerateAndValidateAsync(
                            customer, maskedContext, tone, productCode, productMatches, templateMatches,
                            allowedSourceIds, cancellationToken).ConfigureAwait(false);

                        if (insufficientEvidence)
                        {
                            status = McpToolStatus.NotFound;
                            result = McpToolResponses.RagNoEvidence(traceId);
                        }
                        else if (validRaw is not null)
                        {
                            // ValidateOkDraft (F1 fix) already rejected a null Warnings array before
                            // returning success, so this is safe here.
                            warningCount = validRaw.Warnings!.Count;
                            var draft = BuildDraft(validRaw, allowedSourceIds, maskedContext, customer);
                            status = McpToolStatus.Success;
                            result = McpToolResponses.Success(traceId, draft.SourceIds, new GenerateEmailData(draft));
                        }
                        else
                        {
                            status = McpToolStatus.Error;
                            errorCode = McpToolErrorCode.ModelError;
                            result = McpToolResponses.Error(traceId, McpToolErrorCode.ModelError, ModelErrorMessage, retryable: true);
                        }
                    }
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
        catch (EmailGenerationException ex)
        {
            status = McpToolStatus.Error;
            errorCode = McpToolErrorCode.ModelError;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.ModelError, ModelErrorMessage, ex.Retryable);
        }
        catch (ArgumentException)
        {
            // Defensive fallback: the Validate() pre-check above should make this unreachable.
            status = McpToolStatus.Error;
            errorCode = McpToolErrorCode.InvalidArgument;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "Tham số không hợp lệ.", retryable: false);
        }
        catch (Exception ex)
        {
            status = McpToolStatus.Error;
            errorCode = McpToolErrorCode.InternalError;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);

            // Never pass ex itself to the logger: most providers serialize ex.ToString() (full
            // stack trace + entire InnerException chain), and this project's own exception
            // convention deliberately puts the raw SDK exception in InnerException — exactly what
            // must never reach a log. Only derived-safe values here.
            logger.LogError(
                "MCP tool {ToolName} traceId={TraceId} exceptionType={ExceptionType} errorCode={ErrorCode} retryable={Retryable} durationMs={DurationMs}",
                "generate_email", traceId, ex.GetType().Name, errorCode, false, stopwatch.ElapsedMilliseconds);
        }

        stopwatch.Stop();

        // Audit event (docs/08_RAG_EMAIL_AND_PII_SPEC.md §10), adapted: sessionIdHash is
        // deliberately omitted — CrmCopilot.McpServer is stateless and generate_email's input has
        // no sessionId to hash. No raw prompt/response/model-warning text is ever logged here.
        var customerIdHash = HashForAudit(customerId);
        logger.LogInformation(
            "MCP tool {ToolName} traceId={TraceId} status={Status} durationMs={DurationMs} " +
            "operation={Operation} customerIdHash={CustomerIdHash} retrievedSourceIds={RetrievedSourceIds} " +
            "model={Model} embeddingModel={EmbeddingModel} maskedFieldTypes={MaskedFieldTypes} " +
            "warningCount={WarningCount} errorCode={ErrorCode}",
            "generate_email", traceId, status, stopwatch.ElapsedMilliseconds,
            "generate_email", customerIdHash, string.Join(",", allowedSourceIds),
            EmailGenerationOptions.ModelId, GeminiEmbeddingOptions.ModelId,
            string.Join(",", maskedFieldTypes), warningCount, errorCode ?? "(none)");

        return result;
    }

    private async Task<(bool InsufficientEvidence, RawEmailDraftModel? ValidRaw)> GenerateAndValidateAsync(
        CustomerDto customer,
        MaskedEmailContext maskedContext,
        string tone,
        string? productCode,
        IReadOnlyList<KnowledgeMatch> productMatches,
        IReadOnlyList<KnowledgeMatch> templateMatches,
        IReadOnlyList<string> allowedSourceIds,
        CancellationToken cancellationToken)
    {
        string? correctiveInstruction = null;

        for (var attempt = 1; attempt <= EmailGenerationOptions.MaxAttempts; attempt++)
        {
            var promptContext = new EmailDraftPromptContext(
                maskedContext.MaskedObjective, tone, customer.Segment, maskedContext.Interactions,
                productMatches, templateMatches, productCode, correctiveInstruction,
                ResolveLanguage(customer));

            var raw = await emailDraftGenerator.GenerateAsync(promptContext, cancellationToken).ConfigureAwait(false);

            if (raw is { Status: RawEmailDraftModel.StatusInsufficientEvidence })
            {
                // Terminal on either attempt — a model correctly reporting insufficient evidence
                // is not a defect, never retried.
                return (true, null);
            }

            if (raw is { Status: RawEmailDraftModel.StatusOk })
            {
                var failureReason = ValidateOkDraft(raw, allowedSourceIds, productMatches, templateMatches, productCode, customer);
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

    private static string? Validate(string customerId, string objective, string tone, string? productCode)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return "customerId là bắt buộc.";
        }

        if (string.IsNullOrWhiteSpace(objective))
        {
            return "objective là bắt buộc.";
        }

        if (objective.Length > MaxObjectiveLength)
        {
            return $"objective vượt quá {MaxObjectiveLength} ký tự.";
        }

        if (!AllowedTones.Contains(tone, StringComparer.Ordinal))
        {
            return "tone phải là professional, professional_warm hoặc concise.";
        }

        if (productCode is not null)
        {
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return "productCode rỗng.";
            }

            if (productCode.Length > MaxProductCodeLength)
            {
                return $"productCode vượt quá {MaxProductCodeLength} ký tự.";
            }

            if (!ProductCodePattern.IsMatch(productCode))
            {
                return "productCode không đúng định dạng (ví dụ PRD-SAV-006M).";
            }

            // Defense-in-depth per the P0-07 amendment: check 3's format regex already excludes
            // these shapes for any input reaching this point, so this branch is expected-
            // unreachable in practice, kept as a defensive backstop.
            if (PiiPatterns.Email.IsMatch(productCode) || PiiPatterns.Phone.IsMatch(productCode) ||
                PiiPatterns.DigitRun.IsMatch(productCode) || PiiPatterns.SecretToken.IsMatch(productCode))
            {
                return "productCode chứa dữ liệu không hợp lệ.";
            }
        }

        return null;
    }

    private static string BuildRetrievalQueryText(MaskedEmailContext maskedContext, string? productCode)
    {
        var text = maskedContext.MaskedObjective + "\n" + string.Join("\n", maskedContext.RetrievalQuerySummaries);
        return productCode is not null ? text + "\n" + productCode : text;
    }

    private static IReadOnlyList<string> BuildAllowedSourceIds(
        IReadOnlyList<KnowledgeMatch> productMatches,
        IReadOnlyList<KnowledgeMatch> templateMatches,
        IReadOnlyList<InteractionEvidence> interactions) =>
        [.. productMatches.Select(match => match.SourceId), .. templateMatches.Select(match => match.SourceId), .. interactions.Select(evidence => evidence.SourceId)];

    /// <summary>Ordered checks; first failure short-circuits with a reason string consumed by
    /// <see cref="CorrectiveInstructionFor"/>. Runs on the model's raw, literal output — strictly
    /// before any placeholder restore (see <see cref="BuildDraft"/>/<see cref="RestorePlaceholder"/>),
    /// so a model that wrote the customer's real name directly (bypassing {{CUSTOMER_NAME}}) is
    /// caught here, not laundered by this tool's own subsequent restore step.</summary>
    private static string? ValidateOkDraft(
        RawEmailDraftModel raw,
        IReadOnlyList<string> allowedSourceIds,
        IReadOnlyList<KnowledgeMatch> productMatches,
        IReadOnlyList<KnowledgeMatch> templateMatches,
        string? requestedProductCode,
        CustomerDto customer)
    {
        if (string.IsNullOrWhiteSpace(raw.Subject) || string.IsNullOrWhiteSpace(raw.Body))
        {
            return "empty_subject_or_body";
        }

        // F1 fix: System.Text.Json does not enforce non-null for reference-type properties, so a
        // model returning JSON null for a "required" array is a real, reachable case here — not
        // merely a type-system formality. Both null and empty route to the same reason (the model
        // must cite *something*); a null Warnings array is a distinct schema violation (the JSON
        // Schema's own "warnings" type is "array", not "array|null" — unlike suggestedProductCode's
        // explicit ["string","null"]), so it is treated as generic malformed output.
        if (raw.UsedSourceIds is null || raw.UsedSourceIds.Count == 0)
        {
            return "used_source_ids_empty";
        }

        if (raw.Warnings is null)
        {
            return "invalid_json";
        }

        if (!raw.UsedSourceIds.All(id => allowedSourceIds.Contains(id, StringComparer.Ordinal)))
        {
            return "source_ids_not_subset";
        }

        if (!raw.UsedSourceIds.Any(id =>
                id.StartsWith("kb:product:", StringComparison.Ordinal) ||
                id.StartsWith("kb:email-template:", StringComparison.Ordinal)))
        {
            return "used_source_ids_lack_knowledge_grounding";
        }

        if (raw.SuggestedProductCode is { Length: > 0 } suggested &&
            !productMatches.Any(match => match.Metadata.ProductCode == suggested))
        {
            return "product_code_not_in_evidence";
        }

        if (requestedProductCode is not null &&
            !string.Equals(raw.SuggestedProductCode, requestedProductCode, StringComparison.Ordinal))
        {
            return "suggested_product_code_mismatch_requested";
        }

        var combinedText = raw.Subject + "\n" + raw.Body;

        if (PiiPatterns.Email.IsMatch(combinedText) || PiiPatterns.Phone.IsMatch(combinedText) ||
            PiiPatterns.DigitRun.IsMatch(combinedText) || PiiPatterns.SecretToken.IsMatch(combinedText) ||
            combinedText.Contains(customer.FullName, StringComparison.Ordinal) ||
            combinedText.Contains(customer.Email, StringComparison.Ordinal) ||
            combinedText.Contains(customer.Phone, StringComparison.Ordinal) ||
            combinedText.Contains(customer.AccountReference, StringComparison.Ordinal))
        {
            return "pii_detected_in_output";
        }

        if (!NumericClaimsAreVerified(combinedText, productMatches, templateMatches))
        {
            return "unverified_numeric_claim";
        }

        if (string.Equals(ResolveLanguage(customer), EmailGenerationOptions.DefaultLanguage, StringComparison.OrdinalIgnoreCase) &&
            LacksVietnameseDiacritics(raw.Body!))
        {
            return "unaccented_vietnamese";
        }

        return null;
    }

    private static string ResolveLanguage(CustomerDto customer) =>
        string.IsNullOrWhiteSpace(customer.PreferredLanguage)
            ? EmailGenerationOptions.DefaultLanguage
            : customer.PreferredLanguage;

    /// <summary>
    /// P0-08 live finding: the model produced grammatical but completely unaccented Vietnamese
    /// ("Kinh gui ... chung toi xin gui thong tin tham khao"). Nothing in this pipeline strips
    /// diacritics — the knowledge evidence and the restored customer name are both fully accented,
    /// which is exactly why they still rendered correctly while the model's own prose did not — so
    /// the only correct response is to reject the draft and let the existing single retry ask for
    /// a rewrite. This method never adds diacritics itself.
    ///
    /// Runs on the RAW model body, before <see cref="RestorePlaceholder"/>, and strips the
    /// {{CUSTOMER_NAME}} placeholder first so that neither the customer's accented name nor an
    /// accented product name copied out of evidence can mask otherwise fully unaccented prose.
    ///
    /// Deliberately blunt, never a style judgement: it fires only when a body long enough to be
    /// meaningful (<see cref="MinLetterCountForAccentCheck"/> letters) contains essentially no
    /// Vietnamese-accented letters at all (&lt; <see cref="MinVietnameseAccentRatio"/>). Genuine
    /// Vietnamese prose runs ~15-25% accented letters, an order of magnitude above the threshold,
    /// so quoting a product name or two can never push an unaccented body above it.
    /// </summary>
    private static bool LacksVietnameseDiacritics(string body)
    {
        var prose = body.Replace(CustomerNamePlaceholder, string.Empty, StringComparison.Ordinal);

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
        string text, IReadOnlyList<KnowledgeMatch> productMatches, IReadOnlyList<KnowledgeMatch> templateMatches)
    {
        var evidenceText = string.Join('\n', productMatches.Select(match => match.Content).Concat(templateMatches.Select(match => match.Content)));

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
        "empty_subject_or_body" =>
            "Phản hồi trước có subject hoặc body rỗng dù status là \"ok\". Cung cấp subject và body đầy đủ, hoặc trả status \"insufficient_evidence\" nếu không đủ evidence.",
        "used_source_ids_empty" =>
            "Phản hồi trước có usedSourceIds rỗng dù status là \"ok\". Phải trích dẫn ít nhất một source ID từ evidence đã cung cấp.",
        "source_ids_not_subset" =>
            "Phản hồi trước có usedSourceIds chứa giá trị không nằm trong evidence đã cung cấp. Chỉ được dùng đúng các source ID đã cấp.",
        "used_source_ids_lack_knowledge_grounding" =>
            "Phản hồi trước chỉ trích dẫn source ID không phải kiến thức sản phẩm/template. Phải có ít nhất một source ID dạng kb:product: hoặc kb:email-template: trong usedSourceIds.",
        "product_code_not_in_evidence" =>
            "Phản hồi trước có suggestedProductCode không khớp evidence sản phẩm. Chỉ dùng productCode xuất hiện trong EVIDENCE_PRODUCT, hoặc để null.",
        "suggested_product_code_mismatch_requested" =>
            "REQUESTED_PRODUCT_CODE đã chỉ định một mã cụ thể. suggestedProductCode phải đúng bằng mã đó, không được đề xuất mã khác.",
        "pii_detected_in_output" =>
            "Phản hồi trước chứa dữ liệu dạng email/số điện thoại/số tài khoản/chuỗi giống secret. Giữ nguyên placeholder {{CUSTOMER_NAME}}; không tự chèn thông tin liên hệ thật.",
        "unaccented_vietnamese" =>
            "Phản hồi trước viết tiếng Việt KHÔNG DẤU. Viết lại toàn bộ subject và body bằng tiếng Việt có dấu đầy đủ, dùng đúng chữ Unicode tiếng Việt (ví dụ \"Kính gửi\", không phải \"Kinh gui\"). Giữ nguyên placeholder {{CUSTOMER_NAME}} và không đổi nội dung có căn cứ.",
        "unverified_numeric_claim" =>
            "Phản hồi trước có số liệu (%, số tiền) không xuất hiện trong evidence sản phẩm/template. Chỉ nêu số liệu có trong evidence; nếu evidence không có số liệu, không đề cập số liệu.",
        _ => // "invalid_json" and any other unrecognized reason
            "Phản hồi trước không phải JSON hợp lệ khớp schema đã cung cấp. Hãy trả về CHÍNH XÁC một object JSON khớp schema, không có markdown hay văn bản nào khác ngoài JSON.",
    };

    /// <summary>Only ever called with a <paramref name="raw"/> that already passed
    /// <see cref="ValidateOkDraft"/> (F1 fix) — which rejects null/blank Subject, Body, and null
    /// UsedSourceIds before returning success — so the null-forgiving accesses below are safe by
    /// that established invariant, not by assumption.</summary>
    private static EmailDraftDto BuildDraft(
        RawEmailDraftModel raw, IReadOnlyList<string> allowedSourceIds, MaskedEmailContext maskedContext, CustomerDto customer)
    {
        var finalSourceIds = allowedSourceIds.Where(id => raw.UsedSourceIds!.Contains(id, StringComparer.Ordinal)).ToArray();

        return new EmailDraftDto(
            RestorePlaceholder(raw.Subject!.Trim(), customer.FullName, isBody: false),
            RestorePlaceholder(raw.Body!, customer.FullName, isBody: true),
            raw.SuggestedProductCode,
            finalSourceIds,
            RequiresHumanApproval: true, // always server-forced — never trusts raw.RequiresHumanApproval
            new PiiMaskSummaryDto(maskedContext.MaskedFieldTypes));
    }

    /// <summary>Restores the model's {{CUSTOMER_NAME}} placeholder with the real synthetic name.
    /// If the model dropped/altered the placeholder, falls back to a neutral greeting — Body only
    /// (docs/08_RAG_EMAIL_AND_PII_SPEC.md §6's mandated fallback; a greeting prefix only makes
    /// sense as a body opening, never a subject line).</summary>
    private static string RestorePlaceholder(string text, string customerFullName, bool isBody)
    {
        if (text.Contains(CustomerNamePlaceholder, StringComparison.Ordinal))
        {
            return text.Replace(CustomerNamePlaceholder, customerFullName, StringComparison.Ordinal);
        }

        return isBody ? "Kính gửi Anh/Chị,\n\n" + text : text;
    }

    /// <summary>SHA-256 (a real cryptographic hash function) over the raw customerId, unsalted —
    /// not because SHA-256 is "non-cryptographic", but because customerId is a low-sensitivity
    /// synthetic identifier, not a secret; an unsalted hash is sufficient purely to avoid raw-ID
    /// correlation in log lines, not to resist a determined adversary.
    ///
    /// Accepts null even though the generate_email tool's own customerId parameter is declared
    /// non-nullable (F3 fix): that C# nullability annotation is a compile-time contract only — an
    /// MCP client can still send a JSON-RPC call with a null/missing customerId argument, and this
    /// method is called unconditionally after the try/catch below (every call gets an audit log
    /// entry, including validation failures). Without this, a null customerId would throw inside
    /// Encoding.UTF8.GetBytes *after* Validate() had already produced a clean INVALID_ARGUMENT
    /// result, turning a handled validation error into an unhandled exception.</summary>
    private static string HashForAudit(string? value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))[..16];
}
