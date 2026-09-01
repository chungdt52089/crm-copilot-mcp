using System.ComponentModel;
using System.Diagnostics;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Tools;
using ModelContextProtocol.Server;

namespace CrmCopilot.McpServer.Crm;

/// <summary>
/// P0-14 (PD-021/PD-023): delete_customer, the project's first and only write tool, kept in its own
/// type rather than added to <see cref="CustomerTools"/> so the read tools and the write tool are
/// never one careless edit apart.
///
/// It carries no authorization code of its own, on purpose. ToolAuthorizationFilter is registered as
/// a tools/call request filter in Program.cs, so the role check runs ahead of this method body and
/// therefore ahead of ICrmGateway — a structural guarantee, not one that depends on this method
/// remembering to check first. Only Admin reaches here; RM and Auditor are refused with FORBIDDEN
/// and a DENIED audit line before any of this runs.
/// </summary>
[McpServerToolType]
internal sealed class CustomerAdminTools(ICrmGateway crmGateway, IHttpContextAccessor httpContextAccessor, ILogger<CustomerAdminTools> logger)
{
    private const string NotFoundMessage = "Không tìm thấy khách hàng phù hợp.";
    private const string InternalErrorMessage = "Đã xảy ra lỗi không mong muốn.";

    [McpServerTool(Name = "delete_customer", ReadOnly = false, Destructive = true)]
    [Description("Xoá khách hàng khỏi hệ thống CRM theo customer ID đã xác định. Chỉ dùng khi người dùng yêu cầu xoá rõ ràng và đã nêu đúng mã khách hàng.")]
    public async Task<string> DeleteCustomer(
        [Description("Customer ID cần xoá, ví dụ CUS-0001.")] string customerId,
        CancellationToken cancellationToken = default)
    {
        var traceId = CurrentTraceId();
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
            else if (!CustomerIdFormat.IsValid(customerId))
            {
                // Same defense-in-depth as get_customer: a malformed id must be reported as
                // malformed, not forwarded to the gateway to come back NOT_FOUND — which would
                // assert the id was well-formed and merely absent. On a destructive tool that
                // distinction matters more, not less.
                status = McpToolStatus.Error;
                result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, CustomerIdFormat.InvalidMessage, retryable: false);
            }
            else
            {
                await crmGateway.DeleteCustomerAsync(customerId, cancellationToken).ConfigureAwait(false);
                status = McpToolStatus.Success;
                result = McpToolResponses.Success(traceId, [$"crm:customer:{customerId}"], new DeleteCustomerData(customerId));
            }
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
            // Defensive fallback: the blank check above should make this unreachable —
            // MockCrmGateway.DeleteCustomerAsync's own ThrowIfNullOrWhiteSpace guards the same case.
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "Tham số không hợp lệ.", retryable: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in delete_customer traceId={TraceId}", traceId);
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);
        }

        stopwatch.Stop();

        // Same fields as every other tool's audit line, and deliberately no customerId — a delete is
        // the last place to start logging raw record ids.
        logger.LogInformation(
            "MCP tool {ToolName} traceId={TraceId} status={Status} durationMs={DurationMs}",
            "delete_customer", traceId, status, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private string CurrentTraceId() => httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
}
