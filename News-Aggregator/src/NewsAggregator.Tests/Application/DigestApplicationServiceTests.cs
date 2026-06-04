using NSubstitute;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Application.Services;
using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Application;

/// <summary>
/// Verifies that the use-case coordinator drives the ports in the right order
/// and caches the result — using fakes for every port (no live model needed).
/// </summary>
public sealed class DigestApplicationServiceTests
{
    [Fact]
    public async Task RefreshDigestAsync_collects_enriches_composes_then_caches()
    {
        var items = new List<NewsItem>
        {
            new() { Id = "1", Title = "t", Url = new Uri("https://example.com"), Source = "RSS" },
        };
        var enriched = new List<EnrichedItem>
        {
            new() { Item = items[0], Summary = "s", Category = "AI" },
        };
        var digest = new Digest { GeneratedAt = DateTimeOffset.UnixEpoch };

        var aggregation = Substitute.For<INewsAggregationService>();
        aggregation.CollectAsync(Arg.Any<CancellationToken>()).Returns(items);

        var enrichment = Substitute.For<IEnrichmentWorkflow>();
        enrichment.EnrichAsync(items, Arg.Any<IProgress<Core.Application.AgentProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(enriched);

        var editorial = Substitute.For<IEditorialWorkflow>();
        editorial.ComposeAsync(enriched, Arg.Any<IProgress<Core.Application.AgentProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(digest);

        var cache = Substitute.For<IDigestCache>();

        var sut = new DigestApplicationService(aggregation, enrichment, editorial, cache);

        Digest result = await sut.RefreshDigestAsync();

        Assert.Same(digest, result);
        await cache.Received(1).SetAsync(Arg.Any<string>(), digest, Arg.Any<CancellationToken>());
    }
}
