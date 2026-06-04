using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Ports;

/// <summary>
/// Collects items from all enabled <see cref="INewsSource"/>s and de-duplicates
/// them into a single canonical list. Implemented in Core (it is pure
/// orchestration over the source ports).
/// </summary>
public interface INewsAggregationService
{
    Task<IReadOnlyList<NewsItem>> CollectAsync(CancellationToken cancellationToken = default);
}
