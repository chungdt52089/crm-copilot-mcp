using System.Security.Claims;
using CrmCopilot.Contracts.Chat;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CrmCopilot.Web.Chat;

/// <summary>
/// P0-05 natural-language endpoint (plan §7). Always returns a well-formed
/// <see cref="ChatResponse"/> JSON body — never a bare framework exception page — using HTTP
/// status codes analogous to CrmCopilot.MockCrmApi's existing convention (plan D9).
///
/// P0-12 (WP1): both routes require an authenticated cookie. The authenticated userId is half of
/// the conversation-state key, so one user's session can never resolve another user's context.
/// </summary>
internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", HandleAsync).RequireAuthorization();
        app.MapDelete("/api/chat/sessions/{sessionId}", HandleResetAsync).RequireAuthorization();
        return app;
    }

    /// <summary>Unreachable after RequireAuthorization, but the state key must never silently
    /// fall back to a shared empty string if a future scheme omits the claim.</summary>
    private static string? GetUserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);

    private static async Task<JsonHttpResult<ChatResponse>> HandleAsync(
        ChatRequest request, ClaimsPrincipal user, ChatOrchestrator orchestrator,
        ILogger<ChatOrchestrator> logger, CancellationToken cancellationToken)
    {
        if (GetUserId(user) is not { Length: > 0 } userId)
        {
            return TypedResults.Json(
                new ChatResponse(
                    null, ChatTurnStatus.Error, [], [], null,
                    new ChatTurnError(ChatTurnErrorCode.InternalError, "Phiên đăng nhập không hợp lệ.", Retryable: false)),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        ChatResponse response;
        try
        {
            response = await orchestrator.HandleAsync(
                userId, request.SessionId ?? string.Empty, request.Message ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Last-resort backstop (plan D9) — should be unreachable given ChatOrchestrator's own
            // exhaustive catch/parse handling, but guarantees no raw exception/500 page ever
            // escapes this endpoint.
            logger.LogError(ex, "Unhandled exception in POST /api/chat");
            response = new ChatResponse(
                null, ChatTurnStatus.Error, [], [], null,
                new ChatTurnError(ChatTurnErrorCode.InternalError, "Đã xảy ra lỗi không mong muốn.", Retryable: false));
        }

        return TypedResults.Json(response, statusCode: MapToHttpStatus(response));
    }

    /// <summary>
    /// P0-06 session reset (docs/04 §P0-06 "reset"). Idempotent: a well-formed GUID that was never
    /// used, or already reset, still returns 204. A malformed/blank sessionId returns 400 using the
    /// same <see cref="ChatTurnError"/> shape as <c>/api/chat</c> for a single error vocabulary
    /// across this small API surface, even though this route doesn't return a <see cref="ChatResponse"/>.
    /// Concurrently resetting a session that has an in-flight <c>/api/chat</c> turn is a known,
    /// accepted MVP race (see plan) — no locking is added here.
    /// </summary>
    private static IResult HandleResetAsync(string sessionId, ClaimsPrincipal user, IConversationStateStore stateStore)
    {
        if (GetUserId(user) is not { Length: > 0 } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!SessionIdValidator.TryNormalize(sessionId, out var normalizedSessionId))
        {
            return TypedResults.BadRequest(
                new ChatTurnError(ChatTurnErrorCode.InvalidArgument, SessionIdValidator.InvalidSessionIdMessage, Retryable: false));
        }

        stateStore.Reset(userId, normalizedSessionId);
        return TypedResults.NoContent();
    }

    internal static int MapToHttpStatus(ChatResponse response) => response.Status switch
    {
        ChatTurnStatus.Success => StatusCodes.Status200OK,
        ChatTurnStatus.NotFound => StatusCodes.Status404NotFound,
        ChatTurnStatus.Ambiguous => StatusCodes.Status409Conflict,
        ChatTurnStatus.Error => MapErrorCodeToHttpStatus(response.Error?.Code),
        _ => StatusCodes.Status500InternalServerError,
    };

    private static int MapErrorCodeToHttpStatus(string? code) => code switch
    {
        ChatTurnErrorCode.InvalidArgument or ChatTurnErrorCode.PiiRejected or ChatTurnErrorCode.NameLookupNotSupported or
            ChatTurnErrorCode.CustomerIdRequired or ChatTurnErrorCode.CustomerIdInvalid or
            ChatTurnErrorCode.UnknownTool or ChatTurnErrorCode.DuplicateToolCall or
            ChatTurnErrorCode.MultipleFunctionCallsNotSupported => StatusCodes.Status400BadRequest,
        // P0-13: the MCP boundary refused the tool for this user's role. 403, not 401 — the caller
        // is authenticated; it is the role that is not permitted.
        ChatTurnErrorCode.Forbidden => StatusCodes.Status403Forbidden,
        ChatTurnErrorCode.ToolLoopLimitExceeded => StatusCodes.Status409Conflict,
        ChatTurnErrorCode.UpstreamUnavailable or ChatTurnErrorCode.RagUnavailable or ChatTurnErrorCode.McpUnavailable =>
            StatusCodes.Status503ServiceUnavailable,
        ChatTurnErrorCode.ModelError or ChatTurnErrorCode.McpProtocolError or ChatTurnErrorCode.McpInvalidResponse or
            ChatTurnErrorCode.InternalError => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError,
    };
}
