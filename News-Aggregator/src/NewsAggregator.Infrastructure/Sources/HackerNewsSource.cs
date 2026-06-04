using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Sources;

/// <summary>
/// Adapter over the keyless Hacker News Firebase API. Wired with a named
/// <see cref="HttpClient"/> via <see cref="IHttpClientFactory"/>.
/// </summary>
public sealed class HackerNewsSource : INewsSource
{
    public const string HttpClientName = "hackernews";

    // Bounded fan-out for per-item detail fetches: the id list is one call, but each
    // story detail is a separate request. Cap concurrent in-flight item requests to stay
    // friendly to the public API while avoiding sequential downloads.
    private const int MaxConcurrency = 8;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HackerNewsOptions _options;
    private readonly ILogger<HackerNewsSource> _logger;

    public HackerNewsSource(
        IHttpClientFactory httpClientFactory,
        IOptions<SourceOptions> options,
        ILogger<HackerNewsSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.HackerNews;
        _logger = logger;
    }

    public string SourceName => "HackerNews";

    public async IAsyncEnumerable<NewsItem> FetchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Hacker News source is disabled; yielding no items.");
            yield break;
        }

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

        long[] ids = await FetchStoryIdsAsync(client, cancellationToken);
        if (ids.Length == 0)
        {
            yield break;
        }

        int take = Math.Max(0, _options.MaxItems);
        long[] selected = ids.Length > take ? ids[..take] : ids;

        using var gate = new SemaphoreSlim(MaxConcurrency);
        Task<NewsItem?>[] tasks =
            [.. selected.Select(id => FetchItemAsync(client, id, gate, cancellationToken))];

        // Task.WhenAll preserves task order, so results stay in HN ranking order. Per-item
        // failures surface as null (see FetchItemAsync) rather than faulting the batch.
        NewsItem?[] results = await Task.WhenAll(tasks);

        int mapped = 0;
        foreach (NewsItem? item in results)
        {
            if (item is not null)
            {
                mapped++;
                yield return item;
            }
        }

        _logger.LogInformation(
            "Hacker News: requested {Requested} stories, emitted {Mapped} items.",
            selected.Length,
            mapped);
    }

    private async Task<long[]> FetchStoryIdsAsync(HttpClient client, CancellationToken ct)
    {
        string endpoint = $"{_options.Story}.json";
        try
        {
            long[]? ids = await client.GetFromJsonAsync<long[]>(endpoint, ct);
            if (ids is null || ids.Length == 0)
            {
                _logger.LogWarning("Hacker News {Endpoint} returned no story ids.", endpoint);
                return [];
            }

            return ids;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine cancellation — expected, not an error
        }
        catch (Exception ex)
        {
            // Whole-source failure: the source cannot produce anything. Log and rethrow
            // rather than swallow — Core's aggregation is deliberately fail-fast.
            _logger.LogError(ex, "Failed to fetch Hacker News story ids from {Endpoint}.", endpoint);
            throw;
        }
    }

    private async Task<NewsItem?> FetchItemAsync(
        HttpClient client,
        long id,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            HackerNewsItem? item = await client.GetFromJsonAsync<HackerNewsItem>($"item/{id}.json", ct);
            return MapToNewsItem(item, id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // cancellation must abort the whole source, not be swallowed as a skip
        }
        catch (Exception ex)
        {
            // Transient or malformed single item: skip it, keep the rest of the source
            // alive. Never swallowed silently — every skip is logged.
            _logger.LogWarning(ex, "Skipping Hacker News item {Id}: fetch or parse failed.", id);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private NewsItem? MapToNewsItem(HackerNewsItem? item, long id)
    {
        if (item is null)
        {
            // Deleted/expired items deserialize to JSON null.
            _logger.LogDebug("Hacker News item {Id} returned null (deleted/expired); skipping.", id);
            return null;
        }

        if (item.Deleted || item.Dead)
        {
            _logger.LogDebug("Hacker News item {Id} is deleted or dead; skipping.", id);
            return null;
        }

        if (string.IsNullOrWhiteSpace(item.Title))
        {
            _logger.LogDebug("Hacker News item {Id} has no title; skipping.", id);
            return null;
        }

        return new NewsItem
        {
            Id = id.ToString(CultureInfo.InvariantCulture),
            Title = item.Title,
            Url = ResolveUrl(item, id),
            Source = SourceName,
            Content = string.IsNullOrWhiteSpace(item.Text) ? null : item.Text,
            PublishedAt = item.Time is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
        };
    }

    private static Uri ResolveUrl(HackerNewsItem item, long id)
    {
        if (!string.IsNullOrWhiteSpace(item.Url) &&
            Uri.TryCreate(item.Url, UriKind.Absolute, out Uri? external))
        {
            return external;
        }

        // Text / Ask HN / job posts carry no external url — link to the HN discussion so
        // the required, absolute NewsItem.Url is always satisfiable.
        return new Uri($"https://news.ycombinator.com/item?id={id}");
    }

    /// <summary>
    /// Subset of the Hacker News item payload we map. Bound via
    /// <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> (case-insensitive), so
    /// the API's lowercase fields match without attributes. Extra fields are ignored.
    /// </summary>
    private sealed record HackerNewsItem
    {
        public long Id { get; init; }
        public string? Type { get; init; }
        public string? Title { get; init; }
        public string? Url { get; init; }
        public string? Text { get; init; }
        public long? Time { get; init; }
        public bool Dead { get; init; }
        public bool Deleted { get; init; }
    }
}
