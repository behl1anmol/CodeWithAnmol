namespace NewsAggregator.Tests.Fakes;

/// <summary>
/// Synchronous <see cref="IProgress{T}"/> that records every reported value, so
/// progress can be asserted without the sync-context posting of
/// <see cref="Progress{T}"/> (which is asynchronous and flaky in unit tests).
/// </summary>
public sealed class RecordingProgress<T> : IProgress<T>
{
    public List<T> Reports { get; } = [];

    public void Report(T value) => Reports.Add(value);
}
