using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Ports;

/// <summary>
/// Phase 1 — concurrent (fan-out / fan-in) enrichment. For each item, the
/// Summarizer, Categorizer and Ranker agents run in parallel and their outputs
/// are merged into one <see cref="EnrichedItem"/>.
///
/// The concrete implementation (Infrastructure) builds this on the Microsoft
/// Agent Framework's <c>AgentWorkflowBuilder.BuildConcurrent(...)</c>; Core only
/// sees the port.
/// </summary>
public interface IEnrichmentWorkflow
{
    Task<IReadOnlyList<EnrichedItem>> EnrichAsync(
        IReadOnlyList<NewsItem> items,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
