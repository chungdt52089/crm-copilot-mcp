using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.McpServer.Email;

/// <summary>
/// Everything <see cref="IEmailDraftGenerator"/> needs for one Gemini generateContent attempt.
/// Carries <see cref="MaskedObjective"/> — never the raw <c>objective</c> tool argument — and the
/// already-masked <paramref name="Interactions"/> (P0-07 plan ✏️1). <see cref="CorrectiveInstruction"/>
/// is null on the first attempt and set to a reason-specific string on the single allowed retry.
/// </summary>
internal sealed record EmailDraftPromptContext(
    string MaskedObjective,
    string Tone,
    string Segment,
    IReadOnlyList<InteractionEvidence> Interactions,
    IReadOnlyList<KnowledgeMatch> ProductMatches,
    IReadOnlyList<KnowledgeMatch> TemplateMatches,
    string? RequestedProductCode,
    string? CorrectiveInstruction);
