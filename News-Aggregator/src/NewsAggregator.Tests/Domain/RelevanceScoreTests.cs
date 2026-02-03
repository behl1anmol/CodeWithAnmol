using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Domain;

public sealed class RelevanceScoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void Accepts_values_within_unit_interval(double value)
    {
        var score = new RelevanceScore(value, "reason");
        Assert.Equal(value, score.Value);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Rejects_values_outside_unit_interval(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelevanceScore(value));
    }
}
