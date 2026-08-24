using CrmCopilot.Contracts.Knowledge;

namespace CrmCopilot.Tests.Email.TestSupport;

/// <summary>
/// Deterministic offline stand-in for IKnowledgeRetriever, scoped to Email tests only. Unlike the
/// shared FakeKnowledgeRetriever (one settable Result for every call, used by KnowledgeToolsTests/
/// McpToolProtocolTests and left unmodified here — checkpoint isolation), generate_email issues
/// two SearchAsync calls per invocation (Product, then EmailTemplate) that need independently
/// controllable results/exceptions/captured queries. Routes purely on the single-element
/// DocumentTypes filter EmailTools always passes.
/// </summary>
internal sealed class RoutingKnowledgeRetriever : IKnowledgeRetriever
{
    public KnowledgeSearchResult ProductResult { get; set; } = KnowledgeSearchResult.NoRelevantEvidence;
    public KnowledgeSearchResult TemplateResult { get; set; } = KnowledgeSearchResult.NoRelevantEvidence;
    public Exception? ThrowOnProductSearch { get; set; }
    public Exception? ThrowOnTemplateSearch { get; set; }
    public KnowledgeSearchQuery? LastProductQuery { get; private set; }
    public KnowledgeSearchQuery? LastTemplateQuery { get; private set; }

    public Task<KnowledgeSearchResult> SearchAsync(KnowledgeSearchQuery query, CancellationToken cancellationToken)
    {
        var isProduct = query.DocumentTypes is { Count: 1 } types && types[0] == KnowledgeDocumentType.Product;

        if (isProduct)
        {
            LastProductQuery = query;
            if (ThrowOnProductSearch is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(ProductResult);
        }

        LastTemplateQuery = query;
        if (ThrowOnTemplateSearch is { } templateException)
        {
            throw templateException;
        }

        return Task.FromResult(TemplateResult);
    }
}
