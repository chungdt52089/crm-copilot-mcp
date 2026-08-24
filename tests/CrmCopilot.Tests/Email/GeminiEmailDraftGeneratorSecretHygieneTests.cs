using CrmCopilot.McpServer.Email;

namespace CrmCopilot.Tests.Email;

/// <summary>
/// Verifies GeminiEmailDraftGenerator.WrapFailure and EmailGenerationException's fixed-message
/// shape (P0-07 amendment ✏️27) — mirrors GeminiEmbeddingClientSecretHygieneTests, extended to
/// prove there is no constructor overload that could ever accept a caller-supplied message.
/// </summary>
public class GeminiEmailDraftGeneratorSecretHygieneTests
{
    [Fact]
    public void WrapFailure_InnerMessageContainingSecretLikeString_DoesNotLeakIntoOuterMessage()
    {
        const string fakeSecretMarker = "FAKE_TEST_KEY_1234567890";
        var inner = new InvalidOperationException($"upstream error body: ...?key={fakeSecretMarker}...");

        var wrapped = GeminiEmailDraftGenerator.WrapFailure(retryable: true, inner);

        Assert.DoesNotContain(fakeSecretMarker, wrapped.Message, StringComparison.Ordinal);
        Assert.Same(inner, wrapped.InnerException);
        Assert.Contains(fakeSecretMarker, wrapped.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapFailure_PreservesRetryableFlag()
    {
        var retryable = GeminiEmailDraftGenerator.WrapFailure(retryable: true, new InvalidOperationException());
        var notRetryable = GeminiEmailDraftGenerator.WrapFailure(retryable: false, new InvalidOperationException());

        Assert.True(retryable.Retryable);
        Assert.False(notRetryable.Retryable);
    }

    [Fact]
    public void EmailGenerationException_MessageIsAlwaysFixedLiteral_RegardlessOfInnerException()
    {
        var withInner = new EmailGenerationException(retryable: true, new InvalidOperationException("secret upstream detail"));
        var withoutInner = new EmailGenerationException(retryable: false);

        Assert.Equal(withoutInner.Message, withInner.Message);
        Assert.DoesNotContain("secret upstream detail", withInner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailGenerationException_HasNoConstructorAcceptingAStringMessage()
    {
        var constructors = typeof(EmailGenerationException).GetConstructors(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.All(constructors, ctor => Assert.DoesNotContain(
            ctor.GetParameters(), parameter => parameter.ParameterType == typeof(string)));
    }
}
