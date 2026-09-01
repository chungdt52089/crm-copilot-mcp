namespace CrmCopilot.Contracts.Mcp;

/// <summary>search_product_knowledge success data (docs/07_MCP_TOOL_CONTRACTS.md §6, corrected
/// per the P0-04 plan's D2: the original doc example's "title"/"score" fields do not exist in the
/// P0-03 data model and are never fabricated here).</summary>
public sealed record SearchProductKnowledgeData(IReadOnlyList<KnowledgeMatchDto> Matches);

/// <summary>
/// One retrieved knowledge chunk, as returned over MCP.
/// </summary>
/// <param name="SourceId">e.g. "kb:product:PRD-SAV-006M".</param>
/// <param name="DocumentType">Wire value ("product" or "email_template") — already mapped from
/// CrmCopilot.Contracts.Knowledge.KnowledgeDocumentType by the caller, since Contracts has no
/// dependency on the McpServer-owned wire-mapping class.</param>
/// <param name="Distance">Raw Chroma distance for the configured metric (l2) — lower is better.
/// This is never converted into an invented similarity/accuracy score (P0-04 plan D2; see also
/// CrmCopilot.Contracts.Knowledge.KnowledgeMatch's own remarks from P0-03).</param>
public sealed record KnowledgeMatchDto(
    string SourceId,
    string DocumentType,
    string? ProductCode,
    string Content,
    double Distance);
