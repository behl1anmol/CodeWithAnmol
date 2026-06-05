using System.Runtime.CompilerServices;
using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Sources;

/// <summary>
/// Adapter over config-driven RSS/Atom feeds, parsed with
/// <c>System.ServiceModel.Syndication</c>. Adding a feed is a configuration change, never
/// a code change (Open/Closed). Wired with a named <see cref="HttpClient"/> via
/// <see cref="IHttpClientFactory"/>. Cross-source de-duplication is owned by
/// <c>NewsAggregationService</c>; this source only emits valid <see cref="NewsItem"/>s.
/// </summary>
public sealed class RssNewsSource : INewsSource
{
    public const string HttpClientName = "rss";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RssOptions _options;
    private readonly ILogger<RssNewsSource> _logger;

    public RssNewsSource(
        IHttpClientFactory httpClientFactory,
        IOptions<SourceOptions> options,
        ILogger<RssNewsSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.Rss;
        _logger = logger;
    }

    public string SourceName => "RSS";

    public async IAsyncEnumerable<NewsItem> FetchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("RSS source is disabled; yielding no items.");
            yield break;
        }

        // Parse + validate feed URLs up front: a malformed config entry is skipped (logged),
        // never fabricated into a request. No hardcoded URLs — everything comes from config.
        Uri[] feeds = ResolveFeedUrls();
        if (feeds.Length == 0)
        {
            _logger.LogInformation("RSS source has no valid feeds configured; yielding no items.");
            yield break;
        }

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

        // Bounded fan-out across independent feeds. Task.WhenAll preserves task order, so
        // the merged output is deterministic (feed order, then item order) despite the
        // concurrent fetches. Each task is self-contained: a failing feed returns an empty
        // list rather than faulting the batch (see FetchFeedAsync).
        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
        Task<IReadOnlyList<NewsItem>>[] tasks =
            [.. feeds.Select(feed => FetchFeedAsync(client, feed, gate, cancellationToken))];

        IReadOnlyList<NewsItem>[] results = await Task.WhenAll(tasks);

        int emitted = 0;
        foreach (IReadOnlyList<NewsItem> feedItems in results)
        {
            foreach (NewsItem item in feedItems)
            {
                emitted++;
                yield return item;
            }
        }

        _logger.LogInformation(
            "RSS: fetched {FeedCount} feeds, emitted {Emitted} items.", feeds.Length, emitted);
    }

    private Uri[] ResolveFeedUrls()
    {
        var urls = new List<Uri>(_options.Feeds.Count);
        foreach (string raw in _options.Feeds)
        {
            if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? url))
            {
                urls.Add(url);
            }
            else
            {
                // Bad config entry: skip it, keep the rest of the feeds working.
                _logger.LogWarning("Skipping RSS feed '{Feed}': not an absolute URL.", raw);
            }
        }

        return [.. urls];
    }

    private async Task<IReadOnlyList<NewsItem>> FetchFeedAsync(
        HttpClient client,
        Uri feed,
        SemaphoreSlim gate,
        CancellationToken outerToken)
    {
        await gate.WaitAsync(outerToken);

        // Per-feed deadline covering fetch + read. Linked to the caller's token so a real
        // cancellation still wins; a timeout cancels only this feed's linked token.
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        if (_options.TimeoutSeconds > 0)
        {
            linked.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        }

        try
        {
            using HttpResponseMessage response =
                await client.GetAsync(feed, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(linked.Token);
            SyndicationFeed parsed = ParseFeed(stream);

            int take = Math.Max(0, _options.MaxItemsPerFeed);
            var items = new List<NewsItem>();
            foreach (SyndicationItem entry in parsed.Items.Take(take))
            {
                NewsItem? mapped = MapToNewsItem(entry, feed);
                if (mapped is not null)
                {
                    items.Add(mapped);
                }
            }

            _logger.LogDebug("RSS feed {Feed}: mapped {Count} items.", feed, items.Count);
            return items;
        }
        catch (OperationCanceledException) when (outerToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — fail-fast for the whole source
        }
        catch (Exception ex)
        {
            // Feed-level failure (HTTP error, malformed XML, or per-feed timeout where the
            // caller's token is NOT cancelled): isolate it. Log and drop just this feed so
            // healthy feeds still produce results. Never swallowed silently.
            _logger.LogWarning(ex, "Skipping RSS feed {Feed}: fetch or parse failed.", feed);
            return [];
        }
        finally
        {
            gate.Release();
        }
    }

    private static SyndicationFeed ParseFeed(Stream stream)
    {
        // SyndicationFeed.Load auto-detects RSS 2.0 and Atom 1.0. The reader is non-async
        // but operates over the already-buffered network stream; the awaited network reads
        // above are where cancellation matters.
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { CloseInput = false });
        return SyndicationFeed.Load(reader);
    }

    private NewsItem? MapToNewsItem(SyndicationItem entry, Uri feed)
    {
        string? title = entry.Title?.Text;
        if (string.IsNullOrWhiteSpace(title))
        {
            _logger.LogDebug("RSS feed {Feed}: skipping entry with no title.", feed);
            return null;
        }

        Uri? url = ResolveLink(entry);
        if (url is null)
        {
            // NewsItem.Url is required and must be absolute. With no usable link there is no
            // safe fallback (fabricating a URL would corrupt de-dup), so the entry is dropped.
            _logger.LogDebug("RSS feed {Feed}: skipping entry '{Title}' with no absolute link.", feed, title);
            return null;
        }

        return new NewsItem
        {
            // Prefer the feed-provided id (<guid>/Atom id) as the stable identity; fall back
            // to the absolute link when absent. Core de-dups on URL/title regardless.
            Id = string.IsNullOrWhiteSpace(entry.Id) ? url.AbsoluteUri : entry.Id,
            Title = title,
            Url = url,
            Source = SourceName,
            Content = ResolveContent(entry),
            PublishedAt = ResolvePublishedAt(entry),
        };
    }

    private static Uri? ResolveLink(SyndicationItem entry)
    {
        foreach (SyndicationLink link in entry.Links)
        {
            if (link.Uri is { IsAbsoluteUri: true } uri)
            {
                return uri;
            }
        }

        return null;
    }

    private static string? ResolveContent(SyndicationItem entry)
    {
        // Summary (RSS <description>) first; otherwise the Atom <content>/encoded body.
        // Raw markup is kept as-is (consistent with HackerNewsSource), null when blank.
        if (!string.IsNullOrWhiteSpace(entry.Summary?.Text))
        {
            return entry.Summary.Text;
        }

        if (entry.Content is TextSyndicationContent text && !string.IsNullOrWhiteSpace(text.Text))
        {
            return text.Text;
        }

        return null;
    }

    private static DateTimeOffset? ResolvePublishedAt(SyndicationItem entry)
    {
        // Syndication uses DateTimeOffset.MinValue (default) to mean "absent".
        if (entry.PublishDate != default)
        {
            return entry.PublishDate;
        }

        if (entry.LastUpdatedTime != default)
        {
            return entry.LastUpdatedTime;
        }

        return null;
    }
}
