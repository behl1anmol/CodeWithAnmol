using Microsoft.Agents.AI.Workflows;
using NewsAggregator.Core.Application;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Agents;

namespace NewsAggregator.Infrastructure.Workflows;

/// <summary>
/// Phase 2 adapter. Will build an ordered pipeline (deterministic sort + group
/// executors feeding the Editor agent) with <c>AgentWorkflowBuilder.BuildSequential(...)</c>
/// / <c>WorkflowBuilder</c>, run it via <c>InProcessExecution.RunStreamingAsync(...)</c>,
/// and emit the final <see cref="Digest"/> from the <c>WorkflowOutputEvent</c>.
/// </summary>
public sealed class SequentialEditorialWorkflow : IEditorialWorkflow
{
    private readonly IAgentFactory _agentFactory;

    public SequentialEditorialWorkflow(IAgentFactory agentFactory)
    {
        _agentFactory = agentFactory;
    }

    public Task<Digest> ComposeAsync(
        IReadOnlyList<EnrichedItem> enrichedItems,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // TODO(scaffold): deterministic Sort -> Group executors -> Editor agent
        //   (via _agentFactory.CreateAgent(AgentRole.Editor)); wire with
        //   WorkflowBuilder.AddEdge(...) when mixing executors and agents.
        // No agent workflow logic is implemented yet (foundation only).
        _ = typeof(AgentWorkflowBuilder); // anchors the verified Workflows API surface.
        throw new NotImplementedException(
            "Sequential editorial workflow is not implemented in the scaffold.");
    }
}
