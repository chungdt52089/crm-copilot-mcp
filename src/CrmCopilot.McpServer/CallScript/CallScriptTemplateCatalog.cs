using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;

namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// Reads the call-script playbooks straight from data/knowledge/call-scripts.json through the same
/// <see cref="KnowledgeSourceLoader"/> the ingestion path uses, so the pinned template text is
/// byte-identical to what was embedded into Chroma — there is no second renderer to drift.
///
/// Path resolution is the loader AppContext.BaseDirectory rule, never the current working
/// directory, so this behaves identically under dotnet run and under a test host.
///
/// Loaded once, lazily, and cached: registered as a singleton, so a cold first call pays the file
/// read and every later call is a dictionary hit.
/// </summary>
internal sealed class CallScriptTemplateCatalog : ICallScriptTemplateCatalog
{
    private readonly Lazy<IReadOnlyDictionary<string, CallScriptEvidence>> _byScriptId;

    public CallScriptTemplateCatalog()
        : this(KnowledgeSourceLoader.LoadFromAppBaseDirectory)
    {
    }

    internal CallScriptTemplateCatalog(Func<IReadOnlyList<KnowledgeSourceDocument>> loader)
    {
        _byScriptId = new Lazy<IReadOnlyDictionary<string, CallScriptEvidence>>(() => Build(loader()));
    }

    public CallScriptEvidence? FindByScriptId(string scriptId) =>
        string.IsNullOrWhiteSpace(scriptId) ? null : _byScriptId.Value.GetValueOrDefault(scriptId);

    private static IReadOnlyDictionary<string, CallScriptEvidence> Build(IReadOnlyList<KnowledgeSourceDocument> documents)
    {
        var catalog = new Dictionary<string, CallScriptEvidence>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            // TemplateId is where the loader records a call-script scriptId (see RenderCallScript).
            if (document.DocumentType != KnowledgeDocumentType.CallScript || document.TemplateId is not { Length: > 0 } scriptId)
            {
                continue;
            }

            catalog[scriptId] = new CallScriptEvidence(document.SourceId, scriptId, document.RenderedText);
        }

        return catalog;
    }
}
