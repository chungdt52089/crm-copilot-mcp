namespace CrmCopilot.McpServer.Email;

/// <summary>
/// One interaction, PII-masked, as fed into the Gemini generation prompt's EVIDENCE_INTERACTION
/// block. <see cref="SourceId"/> is <c>crm:interaction:&lt;id&gt;</c> — a legitimate citation
/// target for the model's <c>usedSourceIds</c> (doc07 §7's own canonical example includes one).
/// </summary>
internal sealed record InteractionEvidence(
    string SourceId,
    string Type,
    DateTime OccurredAtUtc,
    string MaskedSummary,
    string MaskedOutcome,
    string? MaskedNextAction);
