using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Sources;
using NewsAggregator.Tests.Fakes;
using NSubstitute;
using Xunit;

namespace NewsAggregator.Tests.Sources;

/// <summary>
/// Behaviour of the Hacker News adapter against canned HTTP responses (never the live
/// API): mapping, the permalink fallback, empty/invalid payloads, MaxItems, the disabled
/// switch, partial failures, cancellation, and deterministic ranking order.
/// </summary>
public sealed class HackerNewsSourceTests
{
    private const string BaseUrl = "https://hacker-news.firebaseio.com/v0/";

    // ---- helpers ------------------------------------------------------------

    private static string TopPath(string story = "topstories") => $"/v0/{story}.json";

    private static string ItemPath(long id) => $"/v0/item/{id}.json";

    // Serialises an anonymous payload with lowercase property names, matching the real HN
    // JSON (and the case-insensitive web defaults GetFromJsonAsync uses).
    private static HttpResponseMessage StoryResponse(object payload)
        => FakeHttpMessageHandler.Json(JsonSerializer.Serialize(payload));

    private static FakeHttpMessageHandler Router(Dictionary<string, Func<HttpResponseMessage>> routes)
        => new(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            return routes.TryGetValue(path, out Func<HttpResponseMessage>? make)
                ? make()
                : FakeHttpMessageHandler.Status(HttpStatusCode.NotFound);
        });

    private static HackerNewsSource BuildSut(FakeHttpMessageHandler handler, HackerNewsOptions? options = null)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HackerNewsSource.HttpClientName).Returns(client);

        var sources = new SourceOptions { HackerNews = options ?? new HackerNewsOptions() };
        return new HackerNewsSource(factory, Options.Create(sources), NullLogger<HackerNewsSource>.Instance);
    }

    private static async Task<List<NewsItem>> DrainAsync(HackerNewsSource sut, CancellationToken ct = default)
    {
        var items = new List<NewsItem>();
        await foreach (NewsItem item in sut.FetchAsync(ct))
        {
            items.Add(item);
        }

        return items;
    }

    // ---- 1. successful retrieval + mapping ----------------------------------

    [Fact]
    public async Task Maps_top_stories_to_news_items()
    {
        const long published = 1_700_000_000;
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[1,2]"),
            [ItemPath(1)] = () => StoryResponse(new
            {
                id = 1L,
                type = "story",
                title = "First",
                url = "https://example.com/1",
                time = published,
            }),
            [ItemPath(2)] = () => StoryResponse(new
            {
                id = 2L,
                type = "story",
                title = "Second",
                url = "https://example.com/2",
                time = published,
            }),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler));

        Assert.Equal(["1", "2"], result.Select(i => i.Id));
        NewsItem first = result[0];
        Assert.Equal("First", first.Title);
        Assert.Equal(new Uri("https://example.com/1"), first.Url);
        Assert.Equal("HackerNews", first.Source);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(published), first.PublishedAt);
        Assert.Null(first.Content);
    }

    // ---- 2. permalink fallback for url-less posts ---------------------------

    [Fact]
    public async Task Text_post_without_url_falls_back_to_hn_permalink()
    {
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[1]"),
            [ItemPath(1)] = () => StoryResponse(new
            {
                id = 1L,
                type = "story",
                title = "Ask HN: anything?",
                text = "<p>body</p>",
                time = 1_700_000_000L,
            }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler)));

        Assert.Equal(new Uri("https://news.ycombinator.com/item?id=1"), item.Url);
        Assert.Equal("<p>body</p>", item.Content);
    }

    // ---- 3 & 4. empty responses ---------------------------------------------

    [Fact]
    public async Task Empty_story_list_yields_nothing()
    {
        var handler = Router(new() { [TopPath()] = () => FakeHttpMessageHandler.Json("[]") });

        Assert.Empty(await DrainAsync(BuildSut(handler)));
    }

    [Fact]
    public async Task Null_story_list_yields_nothing()
    {
        var handler = Router(new() { [TopPath()] = () => FakeHttpMessageHandler.Json("null") });

        Assert.Empty(await DrainAsync(BuildSut(handler)));
    }

    // ---- 3 (invalid payloads): skip rather than fail ------------------------

    [Fact]
    public async Task Skips_deleted_and_dead_items()
    {
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[1,2,3]"),
            [ItemPath(1)] = () => StoryResponse(new { id = 1L, deleted = true }),
            [ItemPath(2)] = () => StoryResponse(new { id = 2L, dead = true, title = "dead story" }),
            [ItemPath(3)] = () => StoryResponse(new { id = 3L, type = "story", title = "alive" }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler)));

        Assert.Equal("3", item.Id);
    }

    [Fact]
    public async Task Skips_null_item_payload()
    {
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[1,2]"),
            [ItemPath(1)] = () => FakeHttpMessageHandler.Json("null"),
            [ItemPath(2)] = () => StoryResponse(new { id = 2L, type = "story", title = "alive" }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler)));

        Assert.Equal("2", item.Id);
    }

    [Fact]
    public async Task Skips_item_without_title()
    {
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[1,2]"),
            [ItemPath(1)] = () => StoryResponse(new { id = 1L, type = "story", url = "https://example.com/1" }),
            [ItemPath(2)] = () => StoryResponse(new { id = 2L, type = "story", title = "has title" }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler)));

        Assert.Equal("2", item.Id);
    }

    // ---- MaxItems -----------------------------------------------------------

    [Fact]
    public async Task Respects_max_items()
    {
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[1,2,3,4,5]"),
            [ItemPath(1)] = () => StoryResponse(new { id = 1L, type = "story", title = "one" }),
            [ItemPath(2)] = () => StoryResponse(new { id = 2L, type = "story", title = "two" }),
            [ItemPath(3)] = () => StoryResponse(new { id = 3L, type = "story", title = "three" }),
        });
        var sut = BuildSut(handler, new HackerNewsOptions { MaxItems = 2 });

        List<NewsItem> result = await DrainAsync(sut);

        Assert.Equal(["1", "2"], result.Select(i => i.Id));
        Assert.Contains(ItemPath(1), handler.RequestedPaths);
        Assert.Contains(ItemPath(2), handler.RequestedPaths);
        Assert.DoesNotContain(ItemPath(3), handler.RequestedPaths);
    }

    // ---- disabled switch ----------------------------------------------------

    [Fact]
    public async Task Disabled_source_yields_nothing_and_makes_no_http_calls()
    {
        var handler = Router(new() { [TopPath()] = () => FakeHttpMessageHandler.Json("[1]") });
        var sut = BuildSut(handler, new HackerNewsOptions { Enabled = false });

        Assert.Empty(await DrainAsync(sut));
        Assert.Empty(handler.RequestedPaths);
    }

    // ---- 5. partial failures ------------------------------------------------

    [Fact]
    public async Task Partial_failure_skips_failed_item_keeps_others()
    {
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[1,2,3]"),
            [ItemPath(1)] = () => StoryResponse(new { id = 1L, type = "story", title = "one" }),
            [ItemPath(2)] = () => FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError),
            [ItemPath(3)] = () => StoryResponse(new { id = 3L, type = "story", title = "three" }),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler));

        Assert.Equal(["1", "3"], result.Select(i => i.Id));
    }

    // ---- 4. cancellation ----------------------------------------------------

    [Fact]
    public async Task Cancellation_throws_operation_canceled()
    {
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[1]"),
            [ItemPath(1)] = () => StoryResponse(new { id = 1L, type = "story", title = "one" }),
        });
        var sut = BuildSut(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DrainAsync(sut, cts.Token));
    }

    // ---- determinism --------------------------------------------------------

    [Fact]
    public async Task Emits_items_in_ranking_order()
    {
        var handler = Router(new()
        {
            [TopPath()] = () => FakeHttpMessageHandler.Json("[3,1,2]"),
            [ItemPath(1)] = () => StoryResponse(new { id = 1L, type = "story", title = "one" }),
            [ItemPath(2)] = () => StoryResponse(new { id = 2L, type = "story", title = "two" }),
            [ItemPath(3)] = () => StoryResponse(new { id = 3L, type = "story", title = "three" }),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler));

        Assert.Equal(["3", "1", "2"], result.Select(i => i.Id));
    }
}
