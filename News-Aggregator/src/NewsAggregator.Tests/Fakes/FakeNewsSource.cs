using System.Runtime.CompilerServices;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Tests.Fakes;

/// <summary>
/// Deterministic <see cref="INewsSource"/> test double. Streams a fixed list of
/// items, honours cancellation per item, and can optionally throw — either
/// immediately (empty item list) or after streaming its items — to drive the
/// fail-fast path in aggregation.
/// </summary>
public sealed class FakeNewsSource : INewsSource
{
    private readonly IReadOnlyList<NewsItem> _items;
    private readonly Exception? _throw;

    public FakeNewsSource(string sourceName, IEnumerable<NewsItem> items, Exception? throwAfter = null)
    {
        SourceName = sourceName;
        _items = items.ToList();
        _throw = throwAfter;
    }

    public string SourceName { get; }

    public async IAsyncEnumerable<NewsItem> FetchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (NewsItem item in _items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }

        if (_throw is not null)
        {
            throw _throw;
        }
    }
}
