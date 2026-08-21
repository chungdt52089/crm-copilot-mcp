using CrmCopilot.McpServer.Knowledge;

namespace CrmCopilot.Tests.Knowledge;

/// <summary>
/// Verifies GeminiEmbeddingClient.WrapFailure — the single place every embedding-failure catch
/// clause routes through — never lets a caught exception's own (possibly verbose, upstream-body-
/// carrying) message reach the outer, commonly-logged KnowledgeEmbeddingException.Message.
/// </summary>
public class GeminiEmbeddingClientSecretHygieneTests
{
    [Fact]
    public void WrapFailure_InnerMessageContainingSecretLikeString_DoesNotLeakIntoOuterMessage()
    {
        const string fakeSecretMarker = "FAKE_TEST_KEY_1234567890";
        var inner = new InvalidOperationException($"upstream error body: ...?key={fakeSecretMarker}...");

        var wrapped = GeminiEmbeddingClient.WrapFailure(retryable: true, inner);

        Assert.DoesNotContain(fakeSecretMarker, wrapped.Message, StringComparison.Ordinal);
        Assert.Same(inner, wrapped.InnerException);
        Assert.Contains(fakeSecretMarker, wrapped.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapFailure_PreservesRetryableFlag()
    {
        var retryable = GeminiEmbeddingClient.WrapFailure(retryable: true, new InvalidOperationException());
        var notRetryable = GeminiEmbeddingClient.WrapFailure(retryable: false, new InvalidOperationException());

        Assert.True(retryable.Retryable);
        Assert.False(notRetryable.Retryable);
    }
}
