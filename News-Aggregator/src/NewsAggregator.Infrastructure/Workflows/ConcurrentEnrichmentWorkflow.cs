using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Agents;

namespace NewsAggregator.Infrastructure.Workflows;

/// <summary>
/// Phase 1 adapter. Will build a fan-out/fan-in workflow with
/// <c>AgentWorkflowBuilder.BuildConcurrent(...)</c> over the Summarizer,
/// Categorizer and Ranker agents, run it via
/// <c>InProcessExecution.RunStreamingAsync(...)</c>, and merge each agent's
/// output into an <see cref="EnrichedItem"/> using a custom aggregator.
/// </summary>
public sealed class ConcurrentEnrichmentWorkflow : IEnrichmentWorkflow
{
    private readonly IAgentFactory _agentFactory;
    private readonly EnrichmentOptions _options;

    public ConcurrentEnrichmentWorkflow(
        IAgentFactory agentFactory,
        IOptions<EnrichmentOptions> options)
    {
        _agentFactory = agentFactory;
        _options = options.Value;
    }

    public Task<IReadOnlyList<EnrichedItem>> EnrichAsync(
        IReadOnlyList<NewsItem> items,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // TODO(scaffold): for each item (bounded by _options.MaxDegreeOfParallelism):
        //   var agents = new[] { Summarizer, Categorizer, Ranker } via _agentFactory;
        //   Workflow wf = AgentWorkflowBuilder.BuildConcurrent(agents, mergeAggregator);
        //   StreamingRun run = await InProcessExecution.RunStreamingAsync(wf, input);
        //   await foreach (WorkflowEvent e in run.WatchStreamAsync()) { ... -> progress }
        // No agent workflow logic is implemented yet (foundation only).
        _ = typeof(AgentWorkflowBuilder); // anchors the verified Workflows API surface.
        throw new NotImplementedException(
            "Concurrent enrichment workflow is not implemented in the scaffold.");
    }
}
