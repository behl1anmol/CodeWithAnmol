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

    // TODO(scaffold): call {Story}.json for ids, then item/{id}.json for the first
    // MaxItems, mapping each into a NewsItem. Uses _httpClientFactory.CreateClient(
    // HttpClientName). No collection logic implemented yet (foundation only).
    public IAsyncEnumerable<NewsItem> FetchAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Hacker News fetching is not implemented in the scaffold.");
}
