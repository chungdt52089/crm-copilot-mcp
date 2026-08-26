using System.ComponentModel;
using System.Diagnostics;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Tools;
using ModelContextProtocol.Server;

namespace CrmCopilot.McpServer.Crm;

/// <summary>
/// P0-10 MCP tool: get_campaigns. Structured CRM retrieval through the existing
/// <see cref="ICrmGateway"/> — no vector search, no model call, no write.
///
/// Scoped to one customer by design (plan D10): there is deliberately no "list every campaign"
/// mode, so a request about one customer can never widen into the whole campaign table.
/// Membership comes from the campaign's explicit EligibleCustomerIds, never from segment matching.
/// </summary>
[McpServerToolType]
internal sealed class CampaignTools(ICrmGateway crmGateway, IHttpContextAccessor httpContextAccessor, ILogger<CampaignTools> logger)
{
    private const int MinLimit = 1;
    private const int MaxLimit = 20;
    private const string NotFoundMessage = "Không tìm thấy khách hàng phù hợp.";
    private const string InternalErrorMessage = "Đã xảy ra lỗi không mong muốn.";

    [McpServerTool(Name = "get_campaigns", ReadOnly = true, Destructive = false)]
    [Description("Lấy các chiến dịch marketing mà một customer ID đã xác định thuộc diện tham gia. Luôn cần customerId — tool này không liệt kê toàn bộ chiến dịch.")]
    public async Task<string> GetCampaigns(
        [Description("Customer ID đã xác định, ví dụ CUS-0001.")] string customerId,
        [Description("Số lượng chiến dịch tối đa (1-20, mặc định 5).")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var traceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        string result;
        string status;
        try
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                status = McpToolStatus.Error;
                result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "customerId là bắt buộc.", retryable: false);
            }
            else if (limit is < MinLimit or > MaxLimit)
            {
                status = McpToolStatus.Error;
                result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, $"limit phải trong khoảng {MinLimit}-{MaxLimit}.", retryable: false);
            }
            else
            {
                var campaigns = await crmGateway.GetCampaignsAsync(customerId, limit, cancellationToken).ConfigureAwait(false);
                var sourceIds = campaigns.Select(campaign => $"crm:campaign:{campaign.Id}").ToArray();
                status = McpToolStatus.Success;
                result = McpToolResponses.Success(traceId, sourceIds, new GetCampaignsData(campaigns));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller's own cancellation — never remapped into a tool error
        }
        catch (CrmNotFoundException)
        {
            status = McpToolStatus.NotFound;
            result = McpToolResponses.NotFound(traceId, McpToolErrorCode.NotFound, NotFoundMessage);
        }
        catch (CrmUpstreamException ex)
        {
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.UpstreamUnavailable, "Không thể kết nối tới hệ thống CRM.", ex.Retryable);
        }
        catch (ArgumentException)
        {
            // Defensive fallback: the pre-checks above already cover blank customerId and the
            // 1-20 limit range the gateway re-validates.
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "Tham số không hợp lệ.", retryable: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in get_campaigns traceId={TraceId}", traceId);
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);
        }

        stopwatch.Stop();
        logger.LogInformation(
            "MCP tool {ToolName} traceId={TraceId} status={Status} durationMs={DurationMs}",
            "get_campaigns", traceId, status, stopwatch.ElapsedMilliseconds);
        return result;
    }
}
