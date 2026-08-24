namespace CrmCopilot.McpServer.Email;

/// <summary>
/// The raw Gemini structured-output schema for generate_email (docs/08_RAG_EMAIL_AND_PII_SPEC.md
/// §8) — deliberately distinct from <see cref="CrmCopilot.Contracts.Mcp.EmailDraftDto"/>, the
/// tool's own final wire shape: this type is model-facing (has <see cref="Status"/>/
/// <see cref="Warnings"/>, uses <see cref="UsedSourceIds"/>), never serialized over MCP itself.
/// EmailTools transforms a validated instance of this into an EmailDraftDto (see the plan's §7.4).
/// Deserialized via CrmJsonOptions.Default (camelCase), matching the exact key names in the JSON
/// schema handed to Gemini's ResponseJsonSchema.
///
/// Every property is nullable despite the JSON schema marking all of them "required": System.Text.Json
/// does not enforce non-null for reference-type properties by default — a model that returns a JSON
/// `null` for any of these (schema presence satisfied, value still null) deserializes successfully
/// with that property set to null here. This type stays honest about that risk instead of assuming
/// non-null; EmailTools.ValidateOkDraft is responsible for rejecting null values as invalid model
/// output (routed through the same one-retry path as any other schema violation), not this type.
/// </summary>
internal sealed record RawEmailDraftModel(
    string? Status,
    string? Subject,
    string? Body,
    string? SuggestedProductCode,
    IReadOnlyList<string>? UsedSourceIds,
    bool RequiresHumanApproval,
    IReadOnlyList<string>? Warnings)
{
    public const string StatusOk = "ok";
    public const string StatusInsufficientEvidence = "insufficient_evidence";
}
