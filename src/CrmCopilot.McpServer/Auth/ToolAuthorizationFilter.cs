using System.Security.Claims;
using CrmCopilot.Contracts.Auth;
using CrmCopilot.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CrmCopilot.McpServer.Auth;

/// <summary>
/// P0-13 (PD-022): the single authorization enforcement point, wired as a tools/call filter rather
/// than copied into each *Tools.cs. Being a filter is what guarantees structurally that the check
/// runs BEFORE the tool body — and therefore before ICrmGateway is ever touched — instead of that
/// guarantee depending on eight separate methods each remembering to check first.
///
/// Refusing produces the same tool-result envelope shape as every other outcome (status "error",
/// code FORBIDDEN), not an MCP protocol-level error, so the Host parses it through the existing
/// McpToolResultParser path with no special casing.
/// </summary>
internal static class ToolAuthorizationFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Apply(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            var toolName = context.Params?.Name ?? string.Empty;
            var role = context.User?.FindFirstValue(McpJwtDefaults.RoleClaim);

            if (ToolPolicy.IsAllowed(role, toolName))
            {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }

            var services = context.Services;
            var traceId = services?.GetService<IHttpContextAccessor>()?.HttpContext?.TraceIdentifier ?? string.Empty;
            var userId = context.User?.FindFirstValue(McpJwtDefaults.UserIdClaim);

            // The demo deliverable (docs/15 §1): a durable audit line proving the refusal happened.
            // Identity and tool name only — never the JWT, never the call's arguments, never a raw
            // customerId (same rule as EmailTools' own audit logging).
            services?.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(ToolAuthorizationFilter).FullName!)
                .LogWarning(
                    "DENIED tool={ToolName} userId={UserId} role={Role} reason={Reason} traceId={TraceId} timestampUtc={TimestampUtc}",
                    toolName,
                    userId ?? "(none)",
                    role ?? "(none)",
                    ToolPolicy.DeniedReason,
                    traceId,
                    DateTime.UtcNow.ToString("O"));

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = McpToolResponses.Forbidden(traceId) }],
            };
        };
}
