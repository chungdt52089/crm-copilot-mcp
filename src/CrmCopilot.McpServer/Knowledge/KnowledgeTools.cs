using System.ComponentModel;
using System.Diagnostics;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.Contracts.Knowledge.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Tools;
using ModelContextProtocol.Server;

namespace CrmCopilot.McpServer.Knowledge;

/// <summary>
/// P0-04 MCP tool: search_product_knowledge (docs/07_MCP_TOOL_CONTRACTS.md §6). First consumer of
/// IKnowledgeRetriever/KnowledgeRetriever registered in P0-03 — that gateway, the Chroma
/// collection schema, and KnowledgeRetrievalOptions.MaxDistance are untouched here (P0-04 plan
/// out-of-scope list). Tool-level query/topK bounds are intentionally tighter than the retriever's
/// own defensive bounds (2000 chars / topK 20) — see the P0-04 plan's D5.
/// </summary>
[McpServerToolType]
internal sealed class KnowledgeTools(IKnowledgeRetriever knowledgeRetriever, IHttpContextAccessor httpContextAccessor, ILogger<KnowledgeTools> logger)
{
    private const int MaxQueryLength = 1000;
    private const int MinTopK = 1;
    private const int MaxTopK = 5;
    private const string InternalErrorMessage = "Đã xảy ra lỗi không mong muốn.";

    /// <summary>
    /// The document types this tool exposes, per docs/07 §6 — deliberately NOT every member of
    /// <see cref="KnowledgeDocumentType"/>. Call scripts share the collection but are outside this
    /// tool's contract: they are reachable only through generate_call_script's own retrieval, so
    /// they can neither be requested here explicitly nor leak in through an unfiltered query.
    /// </summary>
    private static readonly KnowledgeDocumentType[] SearchableDocumentTypes =
        [KnowledgeDocumentType.Product, KnowledgeDocumentType.EmailTemplate];

    [McpServerTool(Name = "search_product_knowledge", ReadOnly = true, Destructive = false)]
    [Description("Tìm product knowledge và email guidance bằng semantic search. Chỉ dùng cho kiến thức sản phẩm/template; không dùng để tìm customer hoặc interaction.")]
    public async Task<string> SearchProductKnowledge(
        [Description("Câu truy vấn tìm kiếm (tối đa 1000 ký tự).")] string query,
        [Description("Số kết quả tối đa (1-5, mặc định 3).")] int topK = 3,
        [Description("Lọc theo loại tài liệu: product, email_template.")] IReadOnlyList<string>? documentTypes = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        string result;
        string status;
        try
        {
            var validationError = Validate(query, topK, documentTypes, out var mappedDocumentTypes);
            if (validationError is not null)
            {
                status = McpToolStatus.Error;
                result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, validationError, retryable: false);
            }
            else
            {
                var searchQuery = new KnowledgeSearchQuery(query, mappedDocumentTypes, topK);
                var searchResult = await knowledgeRetriever.SearchAsync(searchQuery, cancellationToken).ConfigureAwait(false);

                if (searchResult.Status == KnowledgeSearchStatus.NoRelevantEvidence)
                {
                    status = McpToolStatus.NotFound;
                    result = McpToolResponses.RagNoEvidence(traceId);
                }
                else
                {
                    var matches = searchResult.Matches.Select(ToMatchDto).ToArray();
                    var sourceIds = searchResult.Matches.Select(match => match.SourceId).ToArray();
                    status = McpToolStatus.Success;
                    result = McpToolResponses.Success(traceId, sourceIds, new SearchProductKnowledgeData(matches));
                }
            }
        }
        catch (KnowledgeGatewayException ex)
        {
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.RagUnavailable, "Không thể truy xuất knowledge base.", ex.Retryable);
        }
        catch (ArgumentException)
        {
            // Defensive fallback: the Validate() pre-check above should make this unreachable —
            // KnowledgeRetriever.SearchAsync's own bounds are wider than (and a superset of) the
            // ones already validated here.
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "Tham số không hợp lệ.", retryable: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in search_product_knowledge traceId={TraceId}", traceId);
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);
        }

        stopwatch.Stop();
        logger.LogInformation(
            "MCP tool {ToolName} traceId={TraceId} status={Status} durationMs={DurationMs}",
            "search_product_knowledge", traceId, status, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private static string? Validate(
        string query, int topK, IReadOnlyList<string>? documentTypes, out IReadOnlyList<KnowledgeDocumentType>? mappedDocumentTypes)
    {
        mappedDocumentTypes = null;

        if (string.IsNullOrWhiteSpace(query))
        {
            return "query là bắt buộc.";
        }

        if (query.Length > MaxQueryLength)
        {
            return $"query vượt quá {MaxQueryLength} ký tự.";
        }

        if (topK is < MinTopK or > MaxTopK)
        {
            return $"topK phải trong khoảng {MinTopK}-{MaxTopK}.";
        }

        if (documentTypes is { Count: > 0 })
        {
            var mapped = new KnowledgeDocumentType[documentTypes.Count];
            for (var i = 0; i < documentTypes.Count; i++)
            {
                if (!KnowledgeDocumentTypeWireFormat.TryFromWire(documentTypes[i], out var documentType) ||
                    !SearchableDocumentTypes.Contains(documentType))
                {
                    return $"documentTypes chứa giá trị không hợp lệ: '{documentTypes[i]}'.";
                }

                mapped[i] = documentType;
            }

            mappedDocumentTypes = mapped;
        }
        else
        {
            // P0-10 (plan D7). This used to leave the filter null, i.e. unfiltered. That was
            // equivalent to [Product, EmailTemplate] only for as long as those were the only two
            // document types in the collection; once call-script documents were ingested into the
            // same collection an unfiltered query would silently start returning them, changing the
            // results of an already-verified P0 tool — including the canonical PRD-SAV-006M ranking
            // this project's acceptance evidence depends on. The default is now stated explicitly.
            mappedDocumentTypes = SearchableDocumentTypes;
        }

        return null;
    }

    private static KnowledgeMatchDto ToMatchDto(KnowledgeMatch match) => new(
        match.SourceId,
        KnowledgeDocumentTypeWireFormat.ToWire(match.DocumentType),
        match.Metadata.ProductCode,
        match.Content,
        match.Distance);
}
