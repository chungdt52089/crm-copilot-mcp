using System.Text.Json;
using CrmCopilot.Contracts.Api;
using CrmCopilot.Contracts.Mcp;

namespace CrmCopilot.McpServer.Tools;

/// <summary>
/// Builds and serializes <see cref="McpToolResult"/> envelopes for every P0-04 tool. Strictly a
/// response factory/helper — it defines no DTO/record types of its own; every shape it touches is
/// imported from CrmCopilot.Contracts.Mcp, so there is exactly one place (Contracts/Mcp) that owns
/// the wire shapes.
/// </summary>
internal static class McpToolResponses
{
    public const string ForbiddenMessage = "Vai trò của bạn không được phép dùng chức năng này.";

    public static string Success(string traceId, IReadOnlyList<string> sourceIds, object data) =>
        Serialize(new McpToolResult(McpToolStatus.Success, traceId, sourceIds, data, null));

    public static string NotFound(string traceId, string code, string message) =>
        Serialize(new McpToolResult(McpToolStatus.NotFound, traceId, [], null, new McpToolError(code, message, Retryable: false)));

    /// <summary>The RAG "no relevant evidence" outcome (AC-R06): a legitimate result, not a
    /// failure, so <see cref="McpToolResult.Error"/> stays null — distinct from
    /// <see cref="NotFound"/>, which always carries an error detail for customer/interaction
    /// lookups.</summary>
    public static string RagNoEvidence(string traceId) =>
        Serialize(new McpToolResult(McpToolStatus.NotFound, traceId, [], null, null));

    public static string Ambiguous(string traceId, object data) =>
        Serialize(new McpToolResult(McpToolStatus.Ambiguous, traceId, [], data, null));

    public static string Error(string traceId, string code, string message, bool retryable) =>
        Serialize(new McpToolResult(McpToolStatus.Error, traceId, [], null, new McpToolError(code, message, retryable)));

    /// <summary>P0-13: the caller authenticated but their role may not use this tool. Carries no
    /// data and no sourceIds, and the message names neither the tool, the permitted roles, nor any
    /// policy detail — same disclosure rule as CustomerIdFormat.InvalidMessage.</summary>
    public static string Forbidden(string traceId) =>
        Serialize(new McpToolResult(
            McpToolStatus.Error, traceId, [], null,
            new McpToolError(McpToolErrorCode.Forbidden, ForbiddenMessage, Retryable: false)));

    private static string Serialize(McpToolResult result) =>
        JsonSerializer.Serialize(result, CrmJsonOptions.Default);
}
