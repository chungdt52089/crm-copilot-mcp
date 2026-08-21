namespace CrmCopilot.Contracts.Mcp;

/// <summary>
/// Shared MCP tool result envelope (docs/07_MCP_TOOL_CONTRACTS.md §3), common to every P0-04 tool.
/// <see cref="Data"/> is deliberately <see cref="object"/>, not a generic type parameter: the
/// shape differs by status and by tool (see the per-tool *Data records in this namespace), and
/// System.Text.Json's non-generic <c>JsonSerializer.Serialize(object?, JsonSerializerOptions)</c>
/// overload serializes by the value's runtime type, so each tool can assign a different small
/// record without extra polymorphism plumbing. Write-only in P0-04 — nothing deserializes this
/// shape back into <see cref="Data"/>, so the JsonElement/equality pitfall that applies to
/// deserializing into loosely-typed object values does not apply here.
///
/// Locked status/field mapping (resolves docs/07 §3's "error khi có" ambiguity):
/// - success: Data populated, SourceIds populated per tool, Error null.
/// - not_found: Data null, SourceIds empty; Error populated (code=NOT_FOUND) for
///   get_customer/get_interactions, but Error null for search_product_knowledge's "no relevant
///   evidence" outcome — that is a legitimate result (AC-R06), not a failure.
/// - ambiguous: Data populated (candidates), SourceIds empty, Error null — mirrors the P0-02
///   ApiEnvelope 409 AMBIGUOUS_MATCH precedent ("ambiguous is an actionable result, not a
///   failure").
/// - error: Data null, SourceIds empty, Error populated.
/// </summary>
public sealed record McpToolResult(
    string Status,
    string TraceId,
    IReadOnlyList<string> SourceIds,
    object? Data,
    McpToolError? Error);
