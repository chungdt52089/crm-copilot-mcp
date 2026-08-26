using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Email;

namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// Everything <see cref="ICallScriptGenerator"/> needs for one Gemini generateContent attempt.
///
/// Carries <see cref="ResolvedObjective"/> — never the raw objective tool argument — and the
/// already-masked interactions produced by the existing PiiMasker (reused as-is, plan D13).
/// <see cref="Opportunity"/> is at most ONE entry: the tool selects a single opportunity
/// deterministically and never puts several unrelated ones in front of the model (plan Amendment
/// A2). <see cref="CorrectiveInstruction"/> is null on the first attempt and set to a
/// reason-specific string on the single allowed retry.
/// </summary>
internal sealed record CallScriptPromptContext(
    string ResolvedObjective,
    string Segment,
    IReadOnlyList<InteractionEvidence> Interactions,
    SafeOpportunityEvidence? Opportunity,
    IReadOnlyList<CallScriptEvidence> CallScriptMatches,
    IReadOnlyList<KnowledgeMatch> ProductMatches,
    string? RequestedProductCode,
    string? CorrectiveInstruction,
    string Language = CallScriptGenerationOptions.DefaultLanguage);
