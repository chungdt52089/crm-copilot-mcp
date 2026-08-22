using CrmCopilot.Web.Chat;

namespace CrmCopilot.Tests.Web;

/// <summary>
/// Mirrors GeminiEmbeddingClientSecretHygieneTests (P0-03): ChatModelException's Message must
/// always be the fixed string; the caught SDK exception is attached only as InnerException, never
/// interpolated into Message. internal (not private) access via InternalsVisibleTo, same pattern
/// as GeminiEmbeddingClient.WrapFailure.
/// </summary>
public class GeminiChatClientSecretHygieneTests
{
    [Fact]
    public void WrapFailure_MessageIsFixedString_OriginalExceptionOnlyInInnerException()
    {
        var original = new InvalidOperationException("upstream-detail-should-not-leak x-goog-api-key=SECRET");

        var wrapped = GeminiChatClient.WrapFailure(retryable: true, original);

        Assert.Equal("Gemini generateContent call thất bại.", wrapped.Message);
        Assert.DoesNotContain("upstream-detail-should-not-leak", wrapped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", wrapped.Message, StringComparison.Ordinal);
        Assert.Same(original, wrapped.InnerException);
        Assert.True(wrapped.Retryable);
    }

    [Fact]
    public void WrapFailure_NotRetryable_PropagatesFlag()
    {
        var wrapped = GeminiChatClient.WrapFailure(retryable: false, new InvalidOperationException("x"));

        Assert.False(wrapped.Retryable);
    }
}
