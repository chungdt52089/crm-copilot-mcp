namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// The raw Gemini structured-output schema for generate_call_script — deliberately distinct from
/// <see cref="CrmCopilot.Contracts.Mcp.CallScriptDraftDto"/>, the tool final wire shape: this type
/// is model-facing and never serialized over MCP itself.
///
/// Every property is nullable despite the JSON schema marking them required: System.Text.Json does
/// not enforce non-null for reference-type properties, so a model returning a JSON null for any of
/// them deserializes successfully with that property null here. CallScriptTools validation is
/// responsible for rejecting those, not this type.
/// </summary>
internal sealed record RawCallScriptModel(
    string? Status,
    string? Opening,
    IReadOnlyList<string>? DiscoveryQuestions,
    IReadOnlyList<string>? TalkingPoints,
    IReadOnlyList<RawObjectionHandlingItem>? ObjectionHandling,
    string? Closing,
    string? SuggestedProductCode,
    IReadOnlyList<string>? UsedSourceIds,
    bool RequiresHumanApproval,
    IReadOnlyList<string>? Warnings)
{
    public const string StatusOk = "ok";
    public const string StatusInsufficientEvidence = "insufficient_evidence";
}

internal sealed record RawObjectionHandlingItem(string? Objection, string? Response);
