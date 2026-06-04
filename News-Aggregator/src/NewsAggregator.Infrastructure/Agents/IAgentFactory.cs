using Microsoft.Agents.AI;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Agents;

/// <summary>
/// Produces a ready-to-use <see cref="AIAgent"/> for a given role.
///
/// DESIGN NOTE: this port lives in Infrastructure (not Core) on purpose. It
/// returns the framework type <see cref="AIAgent"/>, and the docs' overriding
/// rule (§2.3 / §7.2-F) is that Agent Framework types must never appear in Core.
/// Core therefore depends only on the workflow ports
/// (IEnrichmentWorkflow / IEditorialWorkflow), whose Infrastructure
/// implementations consume this factory.
/// </summary>
public interface IAgentFactory
{
    AIAgent CreateAgent(AgentRole role);
}
