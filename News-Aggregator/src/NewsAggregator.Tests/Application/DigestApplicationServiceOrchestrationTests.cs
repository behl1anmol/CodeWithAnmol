using NewsAggregator.Core.Application;
using NewsAggregator.Core.Application.Services;
using NewsAggregator.Core.Domain;
using NewsAggregator.Tests.Fakes;
using Xunit;

namespace NewsAggregator.Tests.Application;

/// <summary>
/// Behaviour of the use-case coordinator using hand-written fakes (no mocking
/// framework): drives the ports in the right order, wires their data through,
/// caches the result, reports progress, and guards its dependencies.
/// </summary>
public sealed class DigestApplicationServiceOrchestrationTests
{
    private static NewsItem SampleItem()
        => new() { Id = "1", Title = "t", Url = new Uri("https://example.com"), Source = "src" };

    private static EnrichedItem SampleEnriched(NewsItem item)
        => new() { Item = item, Summary = "s", Category = "AI" };

    private static Digest SampleDigest()
        => new() { GeneratedAt = DateTimeOffset.UnixEpoch };

    private static (DigestApplicationService Sut,
        FakeEnrichmentWorkflow Enrichment,
        FakeEditorialWorkflow Editorial,
        FakeDigestCache Cache,
        List<NewsItem> Items,
        List<EnrichedItem> Enriched,
        Digest Digest,
        List<string> Log) Build()
    {
        var items = new List<NewsItem> { SampleItem() };
        var enriched = new List<EnrichedItem> { SampleEnriched(items[0]) };
        var digest = SampleDigest();
        var log = new List<string>();

        var aggregation = new FakeNewsAggregationService(items, log);
        var enrichment = new FakeEnrichmentWorkflow(enriched, log);
        var editorial = new FakeEditorialWorkflow(digest, log);
        var cache = new FakeDigestCache(log);

        var sut = new DigestApplicationService(aggregation, enrichment, editorial, cache);
        return (sut, enrichment, editorial, cache, items, enriched, digest, log);
    }

    [Fact]
    public async Task Drives_ports_in_collect_enrich_compose_cache_order()
    {
        var t = Build();

        await t.Sut.RefreshDigestAsync();

        Assert.Equal(["collect", "enrich", "compose", "cache"], t.Log);
    }

    [Fact]
    public async Task Feeds_collected_items_to_enrichment()
    {
        var t = Build();

        await t.Sut.RefreshDigestAsync();

        Assert.Equal(t.Items, t.Enrichment.ReceivedItems);
    }

    [Fact]
    public async Task Feeds_enriched_items_to_editorial()
    {
        var t = Build();

        await t.Sut.RefreshDigestAsync();

        Assert.Equal(t.Enriched, t.Editorial.ReceivedItems);
    }

    [Fact]
    public async Task Returns_the_composed_digest()
    {
        var t = Build();

        Digest result = await t.Sut.RefreshDigestAsync();

        Assert.Same(t.Digest, result);
    }

    [Fact]
    public async Task Caches_the_composed_digest_once()
    {
        var t = Build();

        await t.Sut.RefreshDigestAsync();

        Assert.Equal(1, t.Cache.SetCount);
        Assert.Same(t.Digest, t.Cache.SetDigest);
        Assert.False(string.IsNullOrWhiteSpace(t.Cache.SetKey));
    }

    [Fact]
    public async Task Reports_progress_for_each_stage()
    {
        var t = Build();
        var progress = new RecordingProgress<AgentProgress>();

        await t.Sut.RefreshDigestAsync(progress);

        string[] stages = [.. progress.Reports.Select(p => p.Stage)];
        Assert.Contains("collecting", stages);
        Assert.Contains("enriching", stages);
        Assert.Contains("composing", stages);
        Assert.Contains("done", stages);
    }

    [Fact]
    public async Task Works_without_a_progress_sink()
    {
        var t = Build();

        Digest result = await t.Sut.RefreshDigestAsync(progress: null);

        Assert.Same(t.Digest, result);
    }

    // ---- dependency guards --------------------------------------------------

    [Fact]
    public void Constructor_rejects_null_aggregation()
        => Assert.Throws<ArgumentNullException>(() => new DigestApplicationService(
            null!,
            new FakeEnrichmentWorkflow([]),
            new FakeEditorialWorkflow(SampleDigest()),
            new FakeDigestCache()));

    [Fact]
    public void Constructor_rejects_null_enrichment()
        => Assert.Throws<ArgumentNullException>(() => new DigestApplicationService(
            new FakeNewsAggregationService([]),
            null!,
            new FakeEditorialWorkflow(SampleDigest()),
            new FakeDigestCache()));

    [Fact]
    public void Constructor_rejects_null_editorial()
        => Assert.Throws<ArgumentNullException>(() => new DigestApplicationService(
            new FakeNewsAggregationService([]),
            new FakeEnrichmentWorkflow([]),
            null!,
            new FakeDigestCache()));

    [Fact]
    public void Constructor_rejects_null_cache()
        => Assert.Throws<ArgumentNullException>(() => new DigestApplicationService(
            new FakeNewsAggregationService([]),
            new FakeEnrichmentWorkflow([]),
            new FakeEditorialWorkflow(SampleDigest()),
            null!));
}
