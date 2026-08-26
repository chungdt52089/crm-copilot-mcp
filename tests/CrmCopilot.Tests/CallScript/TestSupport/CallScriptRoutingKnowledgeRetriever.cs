using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.Tests.CallScript.TestSupport;

/// <summary>
/// Deterministic offline stand-in for IKnowledgeRetriever scoped to call-script tests.
/// generate_call_script issues up to two SearchAsync calls per invocation (CallScript, then
/// Product) that need independently controllable results and captured queries. Routes purely on the
/// single-element DocumentTypes filter the tool always passes.
/// </summary>
internal sealed class CallScriptRoutingKnowledgeRetriever : IKnowledgeRetriever
{
    public KnowledgeSearchResult CallScriptResult { get; set; } = KnowledgeSearchResult.NoRelevantEvidence;
    public KnowledgeSearchResult ProductResult { get; set; } = KnowledgeSearchResult.NoRelevantEvidence;
    public Exception? ThrowOnCallScriptSearch { get; set; }
    public Exception? ThrowOnProductSearch { get; set; }
    public KnowledgeSearchQuery? LastCallScriptQuery { get; private set; }
    public KnowledgeSearchQuery? LastProductQuery { get; private set; }
    public int CallScriptSearchCount { get; private set; }

    public Task<KnowledgeSearchResult> SearchAsync(KnowledgeSearchQuery query, CancellationToken cancellationToken)
    {
        var isCallScript = query.DocumentTypes is { Count: 1 } types && types[0] == KnowledgeDocumentType.CallScript;

        if (isCallScript)
        {
            CallScriptSearchCount++;
            LastCallScriptQuery = query;
            if (ThrowOnCallScriptSearch is { } callScriptException)
            {
                throw callScriptException;
            }

            return Task.FromResult(CallScriptResult);
        }

        LastProductQuery = query;
        if (ThrowOnProductSearch is { } exception)
        {
            throw exception;
        }

        return Task.FromResult(ProductResult);
    }
}
