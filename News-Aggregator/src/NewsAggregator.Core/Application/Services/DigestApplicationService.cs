using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Services;

/// <summary>
/// Coordinates the end-to-end "refresh digest" use-case. This is pure
/// orchestration over Core ports — it contains no source, model, or workflow
/// logic (those live behind the injected ports), so it stays framework-free and
/// trivially unit-testable with fakes.
/// </summary>
public sealed class DigestApplicationService : IDigestApplicationService
{
    private const string CacheKey = "digest:latest";

    private readonly INewsAggregationService _aggregation;
    private readonly IEnrichmentWorkflow _enrichment;
    private readonly IEditorialWorkflow _editorial;
    private readonly IDigestCache _cache;

    public DigestApplicationService(
        INewsAggregationService aggregation,
        IEnrichmentWorkflow enrichment,
        IEditorialWorkflow editorial,
        IDigestCache cache)
    {
        _aggregation = aggregation;
        _enrichment = enrichment;
        _editorial = editorial;
        _cache = cache;
    }

    public async Task<Digest> RefreshDigestAsync(
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new AgentProgress { Stage = "collecting" });
        IReadOnlyList<NewsItem> items = await _aggregation.CollectAsync(cancellationToken);

        progress?.Report(new AgentProgress { Stage = "enriching", TotalCount = items.Count });
        IReadOnlyList<EnrichedItem> enriched =
            await _enrichment.EnrichAsync(items, progress, cancellationToken);

        progress?.Report(new AgentProgress { Stage = "composing" });
        Digest digest = await _editorial.ComposeAsync(enriched, progress, cancellationToken);

        await _cache.SetAsync(CacheKey, digest, cancellationToken);
        progress?.Report(new AgentProgress { Stage = "done" });
        return digest;
    }
}
