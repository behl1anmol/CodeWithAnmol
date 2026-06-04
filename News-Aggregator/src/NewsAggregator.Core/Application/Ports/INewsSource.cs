using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Ports;

/// <summary>
/// A single news source connector (Hacker News, an RSS feed, ...). Adapters live
/// in Infrastructure. Adding a source = adding an implementation; no change to
/// aggregation (Open/Closed).
/// </summary>
public interface INewsSource
{
    /// <summary>Stable display name, e.g. "HackerNews" or "RSS".</summary>
    string SourceName { get; }

    /// <summary>Streams raw items from the source.</summary>
    IAsyncEnumerable<NewsItem> FetchAsync(CancellationToken cancellationToken = default);
}
