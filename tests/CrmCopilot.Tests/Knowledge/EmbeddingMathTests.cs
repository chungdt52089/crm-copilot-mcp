using CrmCopilot.McpServer.Knowledge;

namespace CrmCopilot.Tests.Knowledge;

public class EmbeddingMathTests
{
    [Fact]
    public void L2Normalize_ProducesUnitVector()
    {
        double[] input = [3, 4]; // 3-4-5 triangle — norm is exactly 5

        var result = EmbeddingMath.L2Normalize(input);

        Assert.Equal(0.6, result[0], precision: 10);
        Assert.Equal(0.8, result[1], precision: 10);

        var norm = Math.Sqrt(result.Sum(v => v * v));
        Assert.Equal(1.0, norm, precision: 10);
    }

    [Fact]
    public void L2Normalize_ZeroVector_Throws()
    {
        double[] input = [0, 0, 0];

        Assert.Throws<ArgumentException>(() => EmbeddingMath.L2Normalize(input));
    }

    [Fact]
    public void ValidateDimension_CorrectLength_DoesNotThrow()
    {
        double[] input = new double[768];

        var exception = Record.Exception(() => EmbeddingMath.ValidateDimension(input, 768));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateDimension_WrongLength_Throws()
    {
        double[] input = new double[512];

        Assert.Throws<ArgumentException>(() => EmbeddingMath.ValidateDimension(input, 768));
    }
}
