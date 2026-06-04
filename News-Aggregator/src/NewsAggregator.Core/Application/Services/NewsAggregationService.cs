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
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
    }

    public async Task<IReadOnlyList<NewsItem>> CollectAsync(CancellationToken cancellationToken = default)
    {
        // Fan-out: drain every source concurrently (bounded by the number of
        // registered sources). Fail-fast — any source exception propagates;
        // resilience and logging are Infrastructure concerns, not Core's.
        List<NewsItem>[] perSource = await Task.WhenAll(
            _sources.Select(source => DrainAsync(source, cancellationToken)));

        // Order by Id before de-dup so the surviving representative of a duplicate
        // group is deterministic despite concurrent draining, then keep the first
        // occurrence per canonical URL.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<NewsItem>();
        foreach (NewsItem item in perSource
            .SelectMany(items => items)
            .OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            if (seen.Add(CanonicalKey(item.Url)))
            {
                deduped.Add(item);
            }
        }

        // Sort by publish date descending; nulls last; Id ordinal tie-break so the
        // result is fully deterministic.
        deduped.Sort(static (a, b) =>
        {
            int byDate = Nullable.Compare(b.PublishedAt, a.PublishedAt);
            return byDate != 0 ? byDate : string.CompareOrdinal(a.Id, b.Id);
        });

        return deduped.AsReadOnly();
    }

    private static async Task<List<NewsItem>> DrainAsync(INewsSource source, CancellationToken ct)
    {
        var items = new List<NewsItem>();
        await foreach (NewsItem item in source.FetchAsync(ct).WithCancellation(ct))
        {
            items.Add(item);
        }
        return items;
    }

    private static string CanonicalKey(Uri url)
    {
        // Same-article key: lowercase scheme + authority, drop the fragment, trim a
        // trailing slash, keep path + query (query can distinguish distinct
        // articles served from the same path).
        string scheme = url.Scheme.ToLowerInvariant();
        string authority = url.Authority.ToLowerInvariant();
        string path = url.AbsolutePath.TrimEnd('/');
        return $"{scheme}://{authority}{path}{url.Query}";
    }
}
