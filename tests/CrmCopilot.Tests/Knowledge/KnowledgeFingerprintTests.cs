using CrmCopilot.McpServer.Knowledge;

namespace CrmCopilot.Tests.Knowledge;

public class KnowledgeFingerprintTests
{
    [Fact]
    public void Compute_SameInputs_ProducesIdenticalFingerprint()
    {
        var a = KnowledgeFingerprint.Compute("some rendered text", "gemini-embedding-001", 768, "l2");
        var b = KnowledgeFingerprint.Compute("some rendered text", "gemini-embedding-001", 768, "l2");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_DifferentText_ProducesDifferentFingerprint()
    {
        var a = KnowledgeFingerprint.Compute("text one", "gemini-embedding-001", 768, "l2");
        var b = KnowledgeFingerprint.Compute("text two", "gemini-embedding-001", 768, "l2");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_DifferentModel_ProducesDifferentFingerprint()
    {
        var a = KnowledgeFingerprint.Compute("same text", "gemini-embedding-001", 768, "l2");
        var b = KnowledgeFingerprint.Compute("same text", "gemini-embedding-002", 768, "l2");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_DifferentDimension_ProducesDifferentFingerprint()
    {
        var a = KnowledgeFingerprint.Compute("same text", "gemini-embedding-001", 768, "l2");
        var b = KnowledgeFingerprint.Compute("same text", "gemini-embedding-001", 1536, "l2");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_DifferentDistanceMetric_ProducesDifferentFingerprint()
    {
        var a = KnowledgeFingerprint.Compute("same text", "gemini-embedding-001", 768, "l2");
        var b = KnowledgeFingerprint.Compute("same text", "gemini-embedding-001", 768, "cosine");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_FieldBoundaryShift_DoesNotCollide()
    {
        // Without a field separator, ("ab", "c") and ("a", "bc") would hash identically when
        // naively concatenated. The unit-separator delimiter must prevent that.
        var a = KnowledgeFingerprint.Compute("c", "modelab", 768, "l2");
        var b = KnowledgeFingerprint.Compute("bc", "modela", 768, "l2");

        Assert.NotEqual(a, b);
    }
}
