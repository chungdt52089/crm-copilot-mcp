using System.ComponentModel;
using System.Diagnostics;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Tools;
using ModelContextProtocol.Server;

namespace CrmCopilot.McpServer.Crm;

/// <summary>
/// P0-10 MCP tool: get_opportunities. Structured CRM retrieval through the existing
/// <see cref="ICrmGateway"/> — no vector search, no model call, no write.
///
/// The status filter is validated here and normalized before it reaches the gateway, so an
/// unrecognized value is a caller INVALID_ARGUMENT rather than a silently empty page. Ordering and
/// the filter-before-limit rule are owned one layer down, by the dataset (plan Amendment A1).
/// </summary>
[McpServerToolType]
internal sealed class OpportunityTools(ICrmGateway crmGateway, IHttpContextAccessor httpContextAccessor, ILogger<OpportunityTools> logger)
{
    private const int MinLimit = 1;
    private const int MaxLimit = 20;
    private const string NotFoundMessage = "Không tìm thấy khách hàng phù hợp.";
    private const string InternalErrorMessage = "Đã xảy ra lỗi không mong muốn.";

    [McpServerTool(Name = "get_opportunities", ReadOnly = true, Destructive = false)]
    [Description("Lấy các cơ hội bán (sales opportunity) của một customer ID đã xác định. Dùng status=\"Open\" khi người dùng hỏi về cơ hội đang mở. Không tìm customer theo tên.")]
    public async Task<string> GetOpportunities(
        [Description("Customer ID đã xác định, ví dụ CUS-0001.")] string customerId,
        [Description("Lọc theo trạng thái: Open, Won, Lost, Closed. Bỏ trống để lấy tất cả.")] string? status = null,
        [Description("Số lượng cơ hội tối đa (1-20, mặc định 5).")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var traceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        string result;
        string toolStatus;
        try
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                toolStatus = McpToolStatus.Error;
                result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "customerId là bắt buộc.", retryable: false);
            }
            else if (limit is < MinLimit or > MaxLimit)
            {
                toolStatus = McpToolStatus.Error;
                result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, $"limit phải trong khoảng {MinLimit}-{MaxLimit}.", retryable: false);
            }
            else if (!string.IsNullOrWhiteSpace(status) && !OpportunityStatuses.TryNormalize(status, out _))
            {
                toolStatus = McpToolStatus.Error;
                result = McpToolResponses.Error(
                    traceId,
                    McpToolErrorCode.InvalidArgument,
                    $"status phải là một trong {string.Join(", ", OpportunityStatuses.All)}.",
                    retryable: false);
            }
            else
            {
                var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : Normalize(status);
                var opportunities = await crmGateway
                    .GetOpportunitiesAsync(customerId, normalizedStatus, limit, cancellationToken)
                    .ConfigureAwait(false);

                var sourceIds = opportunities.Select(opportunity => $"crm:opportunity:{opportunity.Id}").ToArray();
                toolStatus = McpToolStatus.Success;
                result = McpToolResponses.Success(traceId, sourceIds, new GetOpportunitiesData(opportunities));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller's own cancellation — never remapped into a tool error
        }
        catch (CrmNotFoundException)
        {
            toolStatus = McpToolStatus.NotFound;
            result = McpToolResponses.NotFound(traceId, McpToolErrorCode.NotFound, NotFoundMessage);
        }
        catch (CrmUpstreamException ex)
        {
            toolStatus = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.UpstreamUnavailable, "Không thể kết nối tới hệ thống CRM.", ex.Retryable);
        }
        catch (ArgumentException)
        {
            // Defensive fallback: the pre-checks above already cover blank customerId, the 1-20
            // limit range, and the status allowlist that the gateway re-validates.
            toolStatus = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "Tham số không hợp lệ.", retryable: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in get_opportunities traceId={TraceId}", traceId);
            toolStatus = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);
        }

        stopwatch.Stop();
        logger.LogInformation(
            "MCP tool {ToolName} traceId={TraceId} status={Status} durationMs={DurationMs}",
            "get_opportunities", traceId, toolStatus, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private static string Normalize(string status)
    {
        OpportunityStatuses.TryNormalize(status, out var normalized);
        return normalized;
    }
}
