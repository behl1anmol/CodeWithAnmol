using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Services;

/// <summary>
/// Fans in items from every enabled <see cref="INewsSource"/> and de-duplicates
/// them by canonical URL/title.
/// </summary>
public sealed class NewsAggregationService : INewsAggregationService
{
    private readonly IEnumerable<INewsSource> _sources;

    public NewsAggregationService(IEnumerable<INewsSource> sources)
    {
        _sources = sources;
    }

    public Task<IReadOnlyList<NewsItem>> CollectAsync(CancellationToken cancellationToken = default)
    {
        // TODO(scaffold): implement collection + de-duplication.
        //   1. Concurrently drain each source's FetchAsync(...) (bounded).
        //   2. De-duplicate by canonical URL / title hash.
        //   3. Return the merged, deduped list.
        // No collection logic is implemented yet (foundation only).
        throw new NotImplementedException(
            "News collection/de-duplication is not implemented in the scaffold.");
    }
}
