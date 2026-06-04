using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Ports;

/// <summary>
/// Phase 2 — sequential editorial pipeline. Deterministic steps (sort by score,
/// group by category) feed an Editor agent that writes section intros and emits
/// the final <see cref="Digest"/>.
///
/// The concrete implementation (Infrastructure) builds this on the Microsoft
/// Agent Framework's <c>AgentWorkflowBuilder.BuildSequential(...)</c> /
/// <c>WorkflowBuilder</c>; Core only sees the port.
/// </summary>
public interface IEditorialWorkflow
{
    Task<Digest> ComposeAsync(
        IReadOnlyList<EnrichedItem> enrichedItems,
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
