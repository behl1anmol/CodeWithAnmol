using Microsoft.Extensions.Time.Testing;
using ShopApi.Diagnostics;
using Xunit;

namespace ShopApi.Tests;

public class OperationTimerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Measure_reports_the_elapsed_duration()
    {
        var time = new FakeTimeProvider(Now);
        var timer = new OperationTimer(time);

        var elapsed = timer.Measure(() => time.Advance(TimeSpan.FromMilliseconds(250)));

        Assert.Equal(TimeSpan.FromMilliseconds(250), elapsed);
    }
}
