using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using NewsAggregator.Core.Application;
using NewsAggregator.Core.Application.Editorial;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Agents;

namespace NewsAggregator.Infrastructure.Workflows;

/// <summary>
/// Editorial pipeline (docs §3.4): the deterministic sort-by-score / group-by-category
/// structure is produced by the pure Core <see cref="DigestComposer"/>; only the section
/// intros come from the Editor agent. Keeping the deterministic logic in Core (rather than
/// custom workflow executors) is the "simplest robust MVP path" from P3 — the framework is
/// used solely to run the one agent and stream its progress.
/// </summary>
/// <remarks>
/// Execution mirrors <see cref="ConcurrentEnrichmentWorkflow"/>, verified against
/// Microsoft.Agents.AI.Workflows 1.9.0: a single-agent
/// <see cref="AgentWorkflowBuilder.BuildSequential(System.Collections.Generic.IEnumerable{AIAgent})"/>
/// workflow is run via <see cref="InProcessExecution"/>; a <see cref="TurnToken"/> with
/// <c>emitEvents: true</c> makes the agent take its turn and surface
/// <see cref="AgentResponseUpdateEvent"/>s, which we accumulate (by the Editor's
/// <see cref="AIAgent.Id"/>) and report as composing progress.
/// </remarks>
public sealed class SequentialEditorialWorkflow : IEditorialWorkflow
{
    private readonly IAgentFactory _agentFactory;
    private readonly TimeProvider _clock;

    public SequentialEditorialWorkflow(IAgentFactory agentFactory, TimeProvider clock)
    {
        _agentFactory = agentFactory;
        _clock = clock;
    }

    public async Task<Digest> ComposeAsync(
        IReadOnlyList<EnrichedItem> enrichedItems,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enrichedItems);

        // Deterministic structure first (pure Core, no LLM).
        IReadOnlyList<DigestSection> sections = DigestComposer.Compose(enrichedItems);

        // No items -> a valid, empty digest. Skip the Editor agent entirely: there is
        // nothing to introduce and no reason to touch a model.
        if (sections.Count == 0)
        {
            return new Digest { GeneratedAt = _clock.GetUtcNow(), Sections = [] };
        }

        string editorReply = await RunEditorAsync(sections, progress, cancellationToken);
        IReadOnlyDictionary<string, string> intros = EditorIntroParser.Parse(editorReply);

        // Map intros back onto sections by category; an unknown/missing intro leaves the
        // section's optional Intro null.
        IReadOnlyList<DigestSection> sectionsWithIntros =
        [
            .. sections.Select(section =>
                intros.TryGetValue(section.Category, out string? intro)
                    ? section with { Intro = intro }
                    : section),
        ];

        return new Digest
        {
            GeneratedAt = _clock.GetUtcNow(),
            Sections = sectionsWithIntros,
        };
    }

    private async Task<string> RunEditorAsync(
        IReadOnlyList<DigestSection> sections,
        IProgress<AgentProgress>? progress,
        CancellationToken cancellationToken)
    {
        AIAgent editor = _agentFactory.CreateAgent(AgentRole.Editor);

        Workflow workflow = AgentWorkflowBuilder.BuildSequential([editor]);
        List<ChatMessage> input = [new ChatMessage(ChatRole.User, BuildPrompt(sections))];

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow, input, cancellationToken: cancellationToken);

        // The agent only responds once it receives a TurnToken; emitEvents:true surfaces
        // AgentResponseUpdateEvents so we can stream composing progress.
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        // The composing payload is constant, so build it once instead of allocating a fresh
        // AgentProgress per streamed delta.
        var composing = new AgentProgress { Role = AgentRole.Editor, Stage = "composing" };

        var reply = new StringBuilder();
        await foreach (WorkflowEvent workflowEvent in run.WatchStreamAsync(cancellationToken))
        {
            if (workflowEvent is not AgentResponseUpdateEvent update
                || !string.Equals(update.ExecutorId, editor.Id, StringComparison.Ordinal))
            {
                continue;
            }

            // Skip empty-text deltas (e.g. the terminal update) so progress tracks actual
            // streamed content rather than every framework event.
            string? text = update.Update?.Text;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            reply.Append(text);
            progress?.Report(composing);
        }

        // WatchStreamAsync ends without throwing on cancellation, so surface a genuine
        // caller cancellation explicitly — fail-fast, matching Core and P2.
        cancellationToken.ThrowIfCancellationRequested();

        return reply.ToString();
    }

    /// <summary>
    /// Renders the deterministic section structure into the Editor prompt: each category
    /// heading followed by its article titles, so the agent can write a fitting intro per
    /// section keyed by the exact category name.
    /// </summary>
    private static string BuildPrompt(IReadOnlyList<DigestSection> sections)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "Write a short intro for each category section of this tech news digest.");
        builder.AppendLine();
        foreach (DigestSection section in sections)
        {
            builder.Append("## ").AppendLine(section.Category);
            foreach (EnrichedItem item in section.Items)
            {
                builder.Append("- ").AppendLine(item.Item.Title);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}
