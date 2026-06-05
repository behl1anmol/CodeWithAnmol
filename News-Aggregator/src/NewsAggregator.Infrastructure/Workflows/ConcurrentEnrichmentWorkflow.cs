using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application;
using NewsAggregator.Core.Application.Enrichment;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Agents;

namespace NewsAggregator.Infrastructure.Workflows;

/// <summary>
/// Fan-out/fan-in enrichment (docs §3.3): for each article the Summarizer,
/// Categorizer and Ranker agents run concurrently via
/// <see cref="AgentWorkflowBuilder.BuildConcurrent(IEnumerable{AIAgent}, System.Func{IList{List{ChatMessage}}, List{ChatMessage}})"/>,
/// and their replies are merged into one <see cref="EnrichedItem"/> by the pure Core
/// <see cref="EnrichedItemAssembler"/>. Cross-article parallelism is bounded by
/// <see cref="EnrichmentOptions.MaxDegreeOfParallelism"/>.
/// </summary>
/// <remarks>
/// Execution details verified against Microsoft.Agents.AI.Workflows 1.9.0:
/// <list type="bullet">
/// <item>The workflow input must be a <see cref="List{T}"/> of <see cref="ChatMessage"/>.</item>
/// <item>A <see cref="TurnToken"/> must be sent to make the agents take their turn; with
/// <c>emitEvents: true</c> the run surfaces <see cref="AgentResponseUpdateEvent"/>s.</item>
/// <item>Each <see cref="AgentResponseUpdateEvent.ExecutorId"/> equals the producing
/// agent's <see cref="AIAgent.Id"/>, so we route updates back to roles by id (the
/// aggregator's message order is not guaranteed, so we parse after the run instead).</item>
/// </list>
/// </remarks>
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

    public async Task<IReadOnlyList<EnrichedItem>> EnrichAsync(
        IReadOnlyList<NewsItem> items,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return [];
        }

        // Bound cross-article parallelism so a local Ollama isn't overwhelmed (docs §3.3
        // "Batching note" — BuildConcurrent only parallelizes the agents over ONE article).
        // Task.WhenAll preserves task order, so the enriched output stays in input order
        // despite the concurrent per-article workflows.
        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxDegreeOfParallelism));
        var reporter = new ProgressReporter(progress, items.Count);
        Task<EnrichedItem>[] tasks =
            [.. items.Select(item => EnrichItemAsync(item, gate, reporter, cancellationToken))];

        return await Task.WhenAll(tasks);
    }

    private async Task<EnrichedItem> EnrichItemAsync(
        NewsItem item,
        SemaphoreSlim gate,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnrichedItem enriched = await RunEnrichmentAsync(item, reporter, cancellationToken);
            reporter.MarkItemCompleted();
            return enriched;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<EnrichedItem> RunEnrichmentAsync(
        NewsItem item,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        // Resolve the role agents. The factory caches them (agents are long-lived and safe
        // to reuse across concurrent runs), so this is cheap; we map each agent's AIAgent.Id
        // to its role to route THIS run's streamed updates back to the right buffer.
        AIAgent summarizer = _agentFactory.CreateAgent(AgentRole.Summarizer);
        AIAgent categorizer = _agentFactory.CreateAgent(AgentRole.Categorizer);
        AIAgent ranker = _agentFactory.CreateAgent(AgentRole.Ranker);

        var roleByExecutorId = new Dictionary<string, AgentRole>(StringComparer.Ordinal)
        {
            [summarizer.Id] = AgentRole.Summarizer,
            [categorizer.Id] = AgentRole.Categorizer,
            [ranker.Id] = AgentRole.Ranker,
        };
        var replies = new Dictionary<AgentRole, StringBuilder>
        {
            [AgentRole.Summarizer] = new StringBuilder(),
            [AgentRole.Categorizer] = new StringBuilder(),
            [AgentRole.Ranker] = new StringBuilder(),
        };

        Workflow workflow = AgentWorkflowBuilder.BuildConcurrent([summarizer, categorizer, ranker]);
        List<ChatMessage> input = [new ChatMessage(ChatRole.User, BuildPrompt(item))];

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow, input, cancellationToken: cancellationToken);

        // Agents only respond once they receive a TurnToken; emitEvents:true makes the run
        // surface AgentResponseUpdateEvents so we can stream live progress (without this the
        // run stalls Idle and never aggregates).
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync(cancellationToken))
        {
            if (workflowEvent is not AgentResponseUpdateEvent update
                || !roleByExecutorId.TryGetValue(update.ExecutorId, out AgentRole role))
            {
                continue;
            }

            string? text = update.Update?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                replies[role].Append(text);
            }

            reporter.ReportAgentUpdate(role);
        }

        // WatchStreamAsync ends the stream WITHOUT throwing when the token is cancelled, so
        // surface a genuine caller cancellation explicitly — fail-fast, matching Core.
        cancellationToken.ThrowIfCancellationRequested();

        // Parsing/merge stays in Core. The assembler is total: even empty or junk agent
        // output yields a valid EnrichedItem rather than throwing.
        return EnrichedItemAssembler.Assemble(
            item,
            replies[AgentRole.Summarizer].ToString(),
            replies[AgentRole.Categorizer].ToString(),
            replies[AgentRole.Ranker].ToString());
    }

    private static string BuildPrompt(NewsItem item)
    {
        var builder = new StringBuilder();
        builder.Append("Title: ").AppendLine(item.Title);
        builder.Append("Source: ").AppendLine(item.Source);
        if (!string.IsNullOrWhiteSpace(item.Content))
        {
            builder.AppendLine();
            builder.AppendLine(item.Content);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Null-safe, thread-safe progress reporting for the bounded fan-out. Agent updates
    /// report the live role; item completion advances the processed count.
    /// </summary>
    private sealed class ProgressReporter
    {
        private readonly IProgress<AgentProgress>? _progress;
        private readonly int _total;
        private int _completed;

        public ProgressReporter(IProgress<AgentProgress>? progress, int total)
        {
            _progress = progress;
            _total = total;
        }

        public void ReportAgentUpdate(AgentRole role)
            => _progress?.Report(new AgentProgress
            {
                Role = role,
                Stage = "enriching",
                ProcessedCount = Volatile.Read(ref _completed),
                TotalCount = _total,
            });

        public void MarkItemCompleted()
        {
            int done = Interlocked.Increment(ref _completed);
            _progress?.Report(new AgentProgress
            {
                Stage = "enriching",
                ProcessedCount = done,
                TotalCount = _total,
            });
        }
    }
}
