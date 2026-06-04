using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Sources;

/// <summary>
/// Adapter over config-driven RSS/Atom feeds, parsed with
/// <c>System.ServiceModel.Syndication</c>. New feeds are a configuration change.
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

    // TODO(scaffold): for each feed URL in _options.Feeds, load with an XmlReader +
    // SyndicationFeed.Load(...), map SyndicationItem -> NewsItem. Invalid feeds are
    // logged and skipped. No parsing logic implemented yet (foundation only).
    public IAsyncEnumerable<NewsItem> FetchAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("RSS/Atom fetching is not implemented in the scaffold.");
}
