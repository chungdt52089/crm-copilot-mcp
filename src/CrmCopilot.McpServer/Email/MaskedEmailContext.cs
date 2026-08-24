namespace CrmCopilot.McpServer.Email;

/// <summary>
/// Output of <see cref="PiiMasker.Mask"/> — everything downstream (retrieval query building,
/// Gemini prompt construction) consumes only this, never the raw <c>objective</c>/interaction
/// fields directly (P0-07 plan §5).
/// </summary>
internal sealed record MaskedEmailContext(
    string MaskedObjective,
    IReadOnlyList<string> RetrievalQuerySummaries,
    IReadOnlyList<InteractionEvidence> Interactions,
    IReadOnlyList<string> MaskedFieldTypes);
