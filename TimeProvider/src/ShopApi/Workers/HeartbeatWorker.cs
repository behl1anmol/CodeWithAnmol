namespace ShopApi.Workers;

public sealed class HeartbeatWorker(TimeProvider timeProvider)
{
    public async Task RunAsync(int beats, IList<DateTimeOffset> log, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);

        for (int i = 0; i < beats && await timer.WaitForNextTickAsync(ct); i++)
        {
            log.Add(timeProvider.GetUtcNow());
        }
    }
}
