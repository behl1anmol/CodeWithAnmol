using Microsoft.Extensions.Time.Testing;
using ShopApi.Workers;
using Xunit;

namespace ShopApi.Tests;

public class HeartbeatWorkerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Worker_records_a_beat_on_each_tick()
    {
        var time = new FakeTimeProvider(Now);
        var worker = new HeartbeatWorker(time);
        var log = new List<DateTimeOffset>();

        var run = worker.RunAsync(beats: 1, log, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(1));
        await run;

        Assert.Single(log);
        Assert.Equal(Now.AddMinutes(1), log[0]);
    }
}
