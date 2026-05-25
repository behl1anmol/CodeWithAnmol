namespace ShopApi.Diagnostics;

public sealed class OperationTimer(TimeProvider timeProvider)
{
    public TimeSpan Measure(Action operation)
    {
        long start = timeProvider.GetTimestamp();
        operation();
        return timeProvider.GetElapsedTime(start);
    }
}
