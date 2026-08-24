namespace CrmCopilot.Contracts.Mcp;

/// <summary>generate_email success data (docs/07_MCP_TOOL_CONTRACTS.md §7).</summary>
public sealed record GenerateEmailData(EmailDraftDto Draft);

/// <summary>
/// The RM-facing email draft. <see cref="RequiresHumanApproval"/> is always true, server-forced —
/// never read from the model's own output field.
/// </summary>
public sealed record EmailDraftDto(
    string Subject,
    string Body,
    string? SuggestedProductCode,
    IReadOnlyList<string> SourceIds,
    bool RequiresHumanApproval,
    PiiMaskSummaryDto PiiMaskSummary);

/// <summary>
/// PII category types actually masked/data-minimized for this call (docs/08_RAG_EMAIL_AND_PII_SPEC.md
/// §6). "name"/"email"/"phone"/"accountReference" are unconditional per call (structural exclusion/
/// placeholder substitution, always in effect); "secret" is conditional (present only when the
/// regex fallback actually matched a secret-token-shaped substring for this call).
/// </summary>
public sealed record PiiMaskSummaryDto(IReadOnlyList<string> MaskedFieldTypes);
