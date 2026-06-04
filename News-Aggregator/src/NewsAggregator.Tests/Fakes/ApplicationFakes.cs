using NewsAggregator.Core.Application;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Tests.Fakes;

/// <summary>
/// Port test doubles for <see cref="DigestApplicationService"/> orchestration
/// tests. Each optionally appends to a shared call-log so the order the
/// coordinator drives the ports in can be asserted, and records what it received.
/// </summary>
public sealed class FakeNewsAggregationService(IEnumerable<NewsItem> items, List<string>? callLog = null)
    : INewsAggregationService
{
    private readonly IReadOnlyList<NewsItem> _items = items.ToList();

    public Task<IReadOnlyList<NewsItem>> CollectAsync(CancellationToken cancellationToken = default)
    {
        callLog?.Add("collect");
        return Task.FromResult(_items);
    }
}

public sealed class FakeEnrichmentWorkflow(IEnumerable<EnrichedItem> result, List<string>? callLog = null)
    : IEnrichmentWorkflow
{
    private readonly IReadOnlyList<EnrichedItem> _result = result.ToList();

    public IReadOnlyList<NewsItem>? ReceivedItems { get; private set; }

    public Task<IReadOnlyList<EnrichedItem>> EnrichAsync(
        IReadOnlyList<NewsItem> items,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        callLog?.Add("enrich");
        ReceivedItems = items;
        return Task.FromResult(_result);
    }
}

public sealed class FakeEditorialWorkflow(Digest result, List<string>? callLog = null)
    : IEditorialWorkflow
{
    public IReadOnlyList<EnrichedItem>? ReceivedItems { get; private set; }

    public Task<Digest> ComposeAsync(
        IReadOnlyList<EnrichedItem> enrichedItems,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        callLog?.Add("compose");
        ReceivedItems = enrichedItems;
        return Task.FromResult(result);
    }
}

public sealed class FakeDigestCache(List<string>? callLog = null) : IDigestCache
{
    private readonly Dictionary<string, Digest> _store = [];

    public string? SetKey { get; private set; }

    public Digest? SetDigest { get; private set; }

    public int SetCount { get; private set; }

    public Task<Digest?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(key, out Digest? digest) ? digest : null);

    public Task SetAsync(string key, Digest digest, CancellationToken cancellationToken = default)
    {
        callLog?.Add("cache");
        SetKey = key;
        SetDigest = digest;
        SetCount++;
        _store[key] = digest;
        return Task.CompletedTask;
    }
}
