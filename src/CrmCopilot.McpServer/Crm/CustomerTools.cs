using System.ComponentModel;
using System.Diagnostics;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Crm.Exceptions;
using CrmCopilot.Contracts.Mcp;
using CrmCopilot.McpServer.Tools;
using ModelContextProtocol.Server;

namespace CrmCopilot.McpServer.Crm;

/// <summary>
/// P0-04 MCP tools: get_customer, get_interactions (docs/07_MCP_TOOL_CONTRACTS.md §4-5). First
/// consumer of ICrmGateway/MockCrmGateway registered in P0-02. Every branch below is planned
/// explicitly in the P0-04 plan's CustomerTools mapping tables — no bare catch(Exception) beyond
/// the single documented INTERNAL_ERROR fallback, and CrmUpstreamException.Message is never
/// interpolated into a *new* exception's Message (it is already a fixed/safe string per its own
/// contract, and is only read here, never re-wrapped).
/// </summary>
[McpServerToolType]
internal sealed class CustomerTools(ICrmGateway crmGateway, IHttpContextAccessor httpContextAccessor, ILogger<CustomerTools> logger)
{
    private const string NotFoundMessage = "Không tìm thấy khách hàng phù hợp.";
    private const string InternalErrorMessage = "Đã xảy ra lỗi không mong muốn.";

    [McpServerTool(Name = "get_customer", ReadOnly = true, Destructive = false)]
    [Description("Tìm hồ sơ khách hàng theo customer ID hoặc chuỗi tên. Dùng trước khi lấy interaction hoặc tạo draft nếu chưa có customer ID chính xác. Không tự chọn khi có nhiều candidate.")]
    public async Task<string> GetCustomer(
        [Description("Customer ID để tra cứu chính xác, ví dụ CUS-0001.")] string? customerId = null,
        [Description("Chuỗi tên khách hàng để tìm kiếm.")] string? query = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = CurrentTraceId();
        var stopwatch = Stopwatch.StartNew();
        var hasCustomerId = !string.IsNullOrWhiteSpace(customerId);
        var hasQuery = !string.IsNullOrWhiteSpace(query);

        string result;
        string status;
        try
        {
            if (hasCustomerId == hasQuery)
            {
                status = McpToolStatus.Error;
                result = McpToolResponses.Error(
                    traceId,
                    McpToolErrorCode.InvalidArgument,
                    hasCustomerId
                        ? "Chỉ được cung cấp một trong customerId hoặc query."
                        : "Phải cung cấp customerId hoặc query.",
                    retryable: false);
            }
            else if (hasCustomerId && !CustomerIdFormat.IsValid(customerId))
            {
                // P0-10 defense in depth. The Host now refuses a malformed id before Gemini sees it,
                // but a direct MCP client bypasses the Host entirely — and without this check a typo
                // like "CS-0003" reached the gateway and came back NOT_FOUND, which asserts the id
                // was well-formed and simply absent. It was not; that is a different failure and
                // must be reported as one. `query` stays free text: it is a name, not an id.
                status = McpToolStatus.Error;
                result = McpToolResponses.Error(
                    traceId, McpToolErrorCode.InvalidArgument, CustomerIdFormat.InvalidMessage, retryable: false);
            }
            else
            {
                var lookupQuery = hasCustomerId
                    ? CustomerLookupQuery.ById(customerId!)
                    : CustomerLookupQuery.ByQuery(query!);

                var lookup = await crmGateway.FindCustomerAsync(lookupQuery, cancellationToken).ConfigureAwait(false);

                (status, result) = lookup.Status switch
                {
                    CustomerLookupStatus.Found => (McpToolStatus.Success, McpToolResponses.Success(
                        traceId,
                        [$"crm:customer:{lookup.Customer!.Id}"],
                        new GetCustomerFoundData(lookup.Customer))),
                    CustomerLookupStatus.Ambiguous => (McpToolStatus.Ambiguous, McpToolResponses.Ambiguous(
                        traceId,
                        new GetCustomerAmbiguousData(lookup.Candidates))),
                    _ => (McpToolStatus.NotFound, McpToolResponses.NotFound(traceId, McpToolErrorCode.NotFound, NotFoundMessage)),
                };
            }
        }
        catch (CrmUpstreamException ex)
        {
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.UpstreamUnavailable, "Không thể kết nối tới hệ thống CRM.", ex.Retryable);
        }
        catch (ArgumentException)
        {
            // Defensive fallback: the hasCustomerId/hasQuery pre-check above should make this
            // unreachable — CustomerLookupQuery.ById/ByQuery only throw for a blank value, and
            // both branches already guard on non-blank.
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "Tham số không hợp lệ.", retryable: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in get_customer traceId={TraceId}", traceId);
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);
        }

        stopwatch.Stop();
        logger.LogInformation(
            "MCP tool {ToolName} traceId={TraceId} status={Status} durationMs={DurationMs}",
            "get_customer", traceId, status, stopwatch.ElapsedMilliseconds);
        return result;
    }

    [McpServerTool(Name = "get_interactions", ReadOnly = true, Destructive = false)]
    [Description("Lấy các tương tác gần nhất của một customer ID đã xác định. Không tìm customer theo tên.")]
    public async Task<string> GetInteractions(
        [Description("Customer ID đã xác định.")] string customerId,
        [Description("Số lượng interaction tối đa (1-20, mặc định 5).")] int limit = 5,
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
            else if (limit is < 1 or > 20)
            {
                status = McpToolStatus.Error;
                result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "limit phải trong khoảng 1-20.", retryable: false);
            }
            else
            {
                var interactions = await crmGateway.GetInteractionsAsync(customerId, limit, cancellationToken).ConfigureAwait(false);
                var sourceIds = interactions.Select(interaction => $"crm:interaction:{interaction.Id}").ToArray();
                status = McpToolStatus.Success;
                result = McpToolResponses.Success(traceId, sourceIds, new GetInteractionsData(interactions));
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
            // Defensive fallback: the customerId/limit pre-checks above should make this
            // unreachable — MockCrmGateway.GetInteractionsAsync's own ArgumentOutOfRangeException
            // guards the same 1-20 range already validated here.
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InvalidArgument, "Tham số không hợp lệ.", retryable: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in get_interactions traceId={TraceId}", traceId);
            status = McpToolStatus.Error;
            result = McpToolResponses.Error(traceId, McpToolErrorCode.InternalError, InternalErrorMessage, retryable: false);
        }

        stopwatch.Stop();
        logger.LogInformation(
            "MCP tool {ToolName} traceId={TraceId} status={Status} durationMs={DurationMs}",
            "get_interactions", traceId, status, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private string CurrentTraceId() => httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
}
