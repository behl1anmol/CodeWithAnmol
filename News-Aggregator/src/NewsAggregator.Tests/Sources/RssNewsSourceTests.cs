using System.Net;
using System.Text;
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
/// Behaviour of the generic RSS/Atom adapter against canned HTTP responses (never the
/// network): mapping, the per-entry skip rules, empty/malformed feeds, multiple feeds,
/// MaxItemsPerFeed, the disabled switch, per-feed timeout + partial-failure isolation, and
/// caller cancellation. Cross-feed de-dup is intentionally NOT exercised here — it belongs
/// to <c>NewsAggregationService</c>.
/// </summary>
public sealed class RssNewsSourceTests
{
    // Two distinct feeds; the fake routes purely on the request's absolute path.
    private const string FeedAUrl = "https://example.com/a";
    private const string FeedBUrl = "https://example.com/b";
    private const string FeedAPath = "/a";
    private const string FeedBPath = "/b";

    // ---- helpers ------------------------------------------------------------

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/rss+xml"),
        };

    private static FakeHttpMessageHandler Router(Dictionary<string, Func<HttpResponseMessage>> routes)
        => new(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            return routes.TryGetValue(path, out Func<HttpResponseMessage>? make)
                ? make()
                : FakeHttpMessageHandler.Status(HttpStatusCode.NotFound);
        });

    private static RssNewsSource BuildSut(
        FakeHttpMessageHandler handler,
        IEnumerable<string> feeds,
        RssOptions? options = null)
    {
        // No BaseAddress: feeds are absolute URLs, matching the real "rss" named client.
        var client = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(RssNewsSource.HttpClientName).Returns(client);

        RssOptions rss = options ?? new RssOptions();
        rss.Feeds = [.. feeds];
        var sources = new SourceOptions { Rss = rss };
        return new RssNewsSource(factory, Options.Create(sources), NullLogger<RssNewsSource>.Instance);
    }

    private static async Task<List<NewsItem>> DrainAsync(RssNewsSource sut, CancellationToken ct = default)
    {
        var items = new List<NewsItem>();
        await foreach (NewsItem item in sut.FetchAsync(ct))
        {
            items.Add(item);
        }

        return items;
    }

    // RSS 2.0 document with the given <item> fragments.
    private static string Rss(params string[] items) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <rss version="2.0">
           <channel>
             <title>Test feed</title>
             <link>https://example.com</link>
             <description>desc</description>
             {string.Join("\n", items)}
           </channel>
         </rss>
         """;

    private static string RssItem(
        string id = "urn:item:1",
        string title = "First post",
        string link = "https://example.com/posts/1",
        string description = "&lt;p&gt;body&lt;/p&gt;",
        string pubDate = "Wed, 02 Oct 2002 15:00:00 GMT") =>
        $"""
         <item>
           <guid>{id}</guid>
           <title>{title}</title>
           <link>{link}</link>
           <description>{description}</description>
           <pubDate>{pubDate}</pubDate>
         </item>
         """;

    // ---- 1. valid feed: mapping --------------------------------------------

    [Fact]
    public async Task Maps_valid_feed_entry_to_news_item()
    {
        var handler = Router(new() { [FeedAPath] = () => Ok(Rss(RssItem())) });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [FeedAUrl])));

        Assert.Equal("urn:item:1", item.Id);
        Assert.Equal("First post", item.Title);
        Assert.Equal(new Uri("https://example.com/posts/1"), item.Url);
        Assert.Equal("RSS", item.Source);
        Assert.Equal("<p>body</p>", item.Content);
        Assert.NotNull(item.PublishedAt);
        Assert.Equal(
            new DateTime(2002, 10, 2, 15, 0, 0, DateTimeKind.Utc),
            item.PublishedAt!.Value.UtcDateTime);
    }

    [Fact]
    public async Task Falls_back_to_link_when_guid_is_missing()
    {
        var handler = Router(new()
        {
            [FeedAPath] = () => Ok(Rss(RssItem(id: "", link: "https://example.com/posts/42"))),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [FeedAUrl])));

        // No <guid> -> the absolute link becomes the stable id.
        Assert.Equal("https://example.com/posts/42", item.Id);
    }

    // ---- per-entry skip rules ----------------------------------------------

    [Fact]
    public async Task Skips_entry_without_title()
    {
        var handler = Router(new()
        {
            [FeedAPath] = () => Ok(Rss(
                RssItem(id: "urn:1", title: "", link: "https://example.com/1"),
                RssItem(id: "urn:2", title: "Kept", link: "https://example.com/2"))),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [FeedAUrl])));

        Assert.Equal("Kept", item.Title);
    }

    [Fact]
    public async Task Skips_entry_without_absolute_link()
    {
        // Relative link cannot satisfy NewsItem's required absolute Url -> entry dropped.
        var handler = Router(new()
        {
            [FeedAPath] = () => Ok(Rss(
                RssItem(id: "urn:1", title: "Relative", link: "/relative/path"),
                RssItem(id: "urn:2", title: "Kept", link: "https://example.com/2"))),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [FeedAUrl])));

        Assert.Equal("Kept", item.Title);
    }

    // ---- 2. empty feed ------------------------------------------------------

    [Fact]
    public async Task Empty_feed_yields_nothing()
    {
        var handler = Router(new() { [FeedAPath] = () => Ok(Rss()) });

        Assert.Empty(await DrainAsync(BuildSut(handler, [FeedAUrl])));
    }

    // ---- 3. malformed feed --------------------------------------------------

    [Fact]
    public async Task Malformed_feed_is_isolated_and_yields_nothing()
    {
        var handler = Router(new() { [FeedAPath] = () => Ok("this is <not> valid xml <<<") });

        // No throw escapes: the bad feed is caught, logged and dropped.
        Assert.Empty(await DrainAsync(BuildSut(handler, [FeedAUrl])));
    }

    // ---- 4. multiple feeds --------------------------------------------------

    [Fact]
    public async Task Merges_items_from_multiple_feeds_in_feed_then_item_order()
    {
        var handler = Router(new()
        {
            [FeedAPath] = () => Ok(Rss(
                RssItem(id: "a1", title: "A1", link: "https://example.com/a1"),
                RssItem(id: "a2", title: "A2", link: "https://example.com/a2"))),
            [FeedBPath] = () => Ok(Rss(
                RssItem(id: "b1", title: "B1", link: "https://example.com/b1"))),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler, [FeedAUrl, FeedBUrl]));

        Assert.Equal(["a1", "a2", "b1"], result.Select(i => i.Id));
    }

    // ---- MaxItemsPerFeed ----------------------------------------------------

    [Fact]
    public async Task Respects_max_items_per_feed()
    {
        var handler = Router(new()
        {
            [FeedAPath] = () => Ok(Rss(
                RssItem(id: "1", title: "one", link: "https://example.com/1"),
                RssItem(id: "2", title: "two", link: "https://example.com/2"),
                RssItem(id: "3", title: "three", link: "https://example.com/3"))),
        });
        var sut = BuildSut(handler, [FeedAUrl], new RssOptions { MaxItemsPerFeed = 2 });

        List<NewsItem> result = await DrainAsync(sut);

        Assert.Equal(["1", "2"], result.Select(i => i.Id));
    }

    // ---- disabled switch ----------------------------------------------------

    [Fact]
    public async Task Disabled_source_yields_nothing_and_makes_no_http_calls()
    {
        var handler = Router(new() { [FeedAPath] = () => Ok(Rss(RssItem())) });
        var sut = BuildSut(handler, [FeedAUrl], new RssOptions { Enabled = false });

        Assert.Empty(await DrainAsync(sut));
        Assert.Empty(handler.RequestedPaths);
    }

    // ---- 5. feed timeout (isolated) -----------------------------------------

    [Fact]
    public async Task Timed_out_feed_is_isolated_and_others_still_produce()
    {
        // Simulate the timeout HttpClient would surface: feed A throws TaskCanceledException
        // while the caller's token stays uncancelled, so it must be isolated (not rethrown).
        var handler = Router(new()
        {
            [FeedAPath] = () => throw new TaskCanceledException("simulated feed timeout"),
            [FeedBPath] = () => Ok(Rss(RssItem(id: "b1", title: "B1", link: "https://example.com/b1"))),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler, [FeedAUrl, FeedBUrl]));

        Assert.Equal(["b1"], result.Select(i => i.Id));
    }

    [Fact]
    public async Task Slow_body_after_headers_hits_per_feed_timeout_and_is_isolated()
    {
        // Feed A sends headers promptly but then stalls while streaming the body. The per-feed
        // TimeoutSeconds must still fire during the body read (regression guard: it previously
        // would not, because SyndicationFeed.Load read the live stream without the token).
        var handler = Router(new()
        {
            [FeedAPath] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingHttpContent() },
            [FeedBPath] = () => Ok(Rss(RssItem(id: "b1", title: "B1", link: "https://example.com/b1"))),
        });
        var sut = BuildSut(handler, [FeedAUrl, FeedBUrl], new RssOptions { TimeoutSeconds = 1 });

        List<NewsItem> result = await DrainAsync(sut);

        Assert.Equal(["b1"], result.Select(i => i.Id));
    }

    // ---- 6. caller cancellation ---------------------------------------------

    [Fact]
    public async Task Cancellation_throws_operation_canceled()
    {
        var handler = Router(new() { [FeedAPath] = () => Ok(Rss(RssItem())) });
        var sut = BuildSut(handler, [FeedAUrl]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DrainAsync(sut, cts.Token));
    }

    // ---- 7. partial failures ------------------------------------------------

    [Fact]
    public async Task Partial_failure_skips_failed_feed_keeps_others()
    {
        var handler = Router(new()
        {
            [FeedAPath] = () => FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError),
            [FeedBPath] = () => Ok(Rss(RssItem(id: "b1", title: "B1", link: "https://example.com/b1"))),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler, [FeedAUrl, FeedBUrl]));

        Assert.Equal(["b1"], result.Select(i => i.Id));
    }

    [Fact]
    public async Task Invalid_feed_url_in_config_is_skipped()
    {
        var handler = Router(new()
        {
            [FeedBPath] = () => Ok(Rss(RssItem(id: "b1", title: "B1", link: "https://example.com/b1"))),
        });

        // "not a url" is not absolute -> dropped before any request; the valid feed still runs.
        List<NewsItem> result = await DrainAsync(BuildSut(handler, ["not a url", FeedBUrl]));

        Assert.Equal(["b1"], result.Select(i => i.Id));
    }

    // ---- Atom support -------------------------------------------------------

    [Fact]
    public async Task Parses_atom_feed()
    {
        const string atom =
            """
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Atom feed</title>
              <id>urn:feed</id>
              <updated>2020-01-01T00:00:00Z</updated>
              <entry>
                <id>urn:atom:1</id>
                <title>Atom entry</title>
                <link href="https://example.com/atom/1" />
                <updated>2020-01-02T08:00:00Z</updated>
                <summary>atom body</summary>
              </entry>
            </feed>
            """;
        var handler = Router(new() { [FeedAPath] = () => Ok(atom) });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [FeedAUrl])));

        Assert.Equal("urn:atom:1", item.Id);
        Assert.Equal("Atom entry", item.Title);
        Assert.Equal(new Uri("https://example.com/atom/1"), item.Url);
        Assert.Equal("atom body", item.Content);
    }

    // HttpContent that returns headers immediately but never finishes streaming its body,
    // unblocking only when the read's cancellation token fires — i.e. a server that stalls
    // mid-body. Used to prove the per-feed deadline covers the body read.
    private sealed class StallingHttpContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => Task.Delay(Timeout.Infinite, cancellationToken);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.Delay(Timeout.Infinite);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
