using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Application.Services;
using NewsAggregator.Core.Domain;
using NewsAggregator.Tests.Fakes;
using Xunit;

namespace NewsAggregator.Tests.Application;

/// <summary>
/// Behaviour of the Core aggregation: merge across sources, de-duplicate by
/// canonical URL, sort by publish date descending, deterministically.
/// </summary>
public sealed class NewsAggregationServiceTests
{
    private static NewsItem Item(string id, string url, DateTimeOffset? published = null)
        => new() { Id = id, Title = "t", Url = new Uri(url), Source = "src", PublishedAt = published };

    private static NewsAggregationService Sut(params INewsSource[] sources)
        => new(sources);

    // ---- merge --------------------------------------------------------------

    [Fact]
    public async Task Merges_items_from_all_sources()
    {
        var sut = Sut(
            new FakeNewsSource("a", [Item("1", "https://a.test/1")]),
            new FakeNewsSource("b", [Item("2", "https://b.test/2")]));

        IReadOnlyList<NewsItem> result = await sut.CollectAsync();

        Assert.Equal(["1", "2"], result.Select(i => i.Id).OrderBy(id => id));
    }

    // ---- de-duplication -----------------------------------------------------

    [Theory]
    [InlineData("https://example.com/article", "https://example.com/article")]            // identical
    [InlineData("https://example.com/article", "HTTPS://EXAMPLE.COM/article")]            // scheme/host case
    [InlineData("https://example.com/article", "https://example.com/article/")]           // trailing slash
    [InlineData("https://example.com/article", "https://example.com/article#section")]    // fragment
    public async Task Removes_duplicates_by_canonical_url(string urlA, string urlB)
    {
        var sut = Sut(
            new FakeNewsSource("a", [Item("1", urlA)]),
            new FakeNewsSource("b", [Item("2", urlB)]));

        IReadOnlyList<NewsItem> result = await sut.CollectAsync();

        Assert.Single(result);
    }

    [Fact]
    public async Task Keeps_items_when_query_string_differs()
    {
        var sut = Sut(new FakeNewsSource("a",
        [
            Item("1", "https://example.com/p?id=1"),
            Item("2", "https://example.com/p?id=2"),
        ]));

        IReadOnlyList<NewsItem> result = await sut.CollectAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Duplicate_survivor_is_deterministic_lowest_id()
    {
        // Same canonical URL from two sources; the item with the smaller Id wins
        // regardless of source ordering.
        var sut = Sut(
            new FakeNewsSource("a", [Item("zzz", "https://example.com/x")]),
            new FakeNewsSource("b", [Item("aaa", "https://example.com/x")]));

        IReadOnlyList<NewsItem> result = await sut.CollectAsync();

        Assert.Equal("aaa", Assert.Single(result).Id);
    }

    // ---- ordering -----------------------------------------------------------

    [Fact]
    public async Task Sorts_by_published_date_descending()
    {
        var older = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sut = Sut(new FakeNewsSource("a",
        [
            Item("1", "https://example.com/old", older),
            Item("2", "https://example.com/new", newer),
        ]));

        IReadOnlyList<NewsItem> result = await sut.CollectAsync();

        Assert.Equal(["2", "1"], result.Select(i => i.Id));
    }

    [Fact]
    public async Task Orders_items_without_publish_date_last()
    {
        var dated = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sut = Sut(new FakeNewsSource("a",
        [
            Item("1", "https://example.com/none", published: null),
            Item("2", "https://example.com/dated", dated),
        ]));

        IReadOnlyList<NewsItem> result = await sut.CollectAsync();

        Assert.Equal(["2", "1"], result.Select(i => i.Id));
    }

    [Fact]
    public async Task Breaks_date_ties_by_id_ordinal()
    {
        var same = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sut = Sut(new FakeNewsSource("a",
        [
            Item("b", "https://example.com/b", same),
            Item("a", "https://example.com/a", same),
        ]));

        IReadOnlyList<NewsItem> result = await sut.CollectAsync();

        Assert.Equal(["a", "b"], result.Select(i => i.Id));
    }

    [Fact]
    public async Task Result_is_deterministic_across_runs()
    {
        var same = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        NewsAggregationService Build() => Sut(
            new FakeNewsSource("a", [Item("3", "https://example.com/3", same)]),
            new FakeNewsSource("b", [Item("1", "https://example.com/1", same), Item("2", "https://example.com/2", same)]));

        IReadOnlyList<NewsItem> first = await Build().CollectAsync();
        IReadOnlyList<NewsItem> second = await Build().CollectAsync();

        Assert.Equal(first.Select(i => i.Id), second.Select(i => i.Id));
        Assert.Equal(["1", "2", "3"], first.Select(i => i.Id));
    }

    // ---- edge cases ---------------------------------------------------------

    [Fact]
    public async Task No_sources_returns_empty()
    {
        IReadOnlyList<NewsItem> result = await Sut().CollectAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task Source_yielding_nothing_returns_empty()
    {
        IReadOnlyList<NewsItem> result = await Sut(new FakeNewsSource("a", [])).CollectAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task Result_is_read_only()
    {
        IReadOnlyList<NewsItem> result =
            await Sut(new FakeNewsSource("a", [Item("1", "https://example.com/1")])).CollectAsync();

        Assert.True(((ICollection<NewsItem>)result).IsReadOnly);
    }

    // ---- failure paths ------------------------------------------------------

    [Fact]
    public async Task Propagates_exception_from_a_failing_source()
    {
        var boom = new InvalidOperationException("source down");
        var sut = Sut(
            new FakeNewsSource("ok", [Item("1", "https://example.com/1")]),
            new FakeNewsSource("bad", [], throwAfter: boom));

        InvalidOperationException thrown =
            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CollectAsync());
        Assert.Same(boom, thrown);
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = Sut(new FakeNewsSource("a", [Item("1", "https://example.com/1")]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.CollectAsync(cts.Token));
    }
}
