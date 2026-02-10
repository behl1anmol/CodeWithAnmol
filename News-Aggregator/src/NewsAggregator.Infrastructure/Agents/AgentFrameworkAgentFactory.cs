using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Models;

namespace NewsAggregator.Infrastructure.Agents;

/// <summary>
/// Builds a <see cref="ChatClientAgent"/> per role over the provider's
/// <see cref="IChatClient"/>. This is where Microsoft.Extensions.AI meets the
/// Microsoft Agent Framework — the only place agents are constructed.
/// </summary>
/// <remarks>
/// Agents are <b>cached one-per-role</b> and reused for the lifetime of this
/// (singleton) factory. Each <see cref="IChatClient"/> the factory builds owns an
/// HTTP-level pipeline (e.g. <c>OllamaApiClient</c> + its <c>HttpClient</c>), and those
/// are meant to be long-lived, not constructed per request — building one per agent per
/// article would retain provider/HTTP resources on every digest refresh. A
/// <see cref="ChatClientAgent"/> is stateless across runs and safe to reuse concurrently,
/// so one instance per role serves every workflow run.
/// </remarks>
public sealed class AgentFrameworkAgentFactory : IAgentFactory
{
    private readonly IChatModelProvider _modelProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ConcurrentDictionary<AgentRole, Lazy<AIAgent>> _agents = new();

    public AgentFrameworkAgentFactory(
        IChatModelProvider modelProvider,
        IChatClientFactory chatClientFactory)
    {
        _modelProvider = modelProvider;
        _chatClientFactory = chatClientFactory;
    }

    public AIAgent CreateAgent(AgentRole role)
        // Lazy ensures the (expensive, HTTP-backed) client is built exactly once per role,
        // even if two workflow runs ask for the same role concurrently on first use.
        => _agents.GetOrAdd(
            role,
            r => new Lazy<AIAgent>(() => BuildAgent(r), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private AIAgent BuildAgent(AgentRole role)
    {
        ChatModelDescriptor descriptor = _modelProvider.Describe(role);
        IChatClient chatClient = _chatClientFactory.Create(descriptor);

        // Verified pattern (docs §3.1 / §4): IChatClient -> AIAgent.
        return new ChatClientAgent(chatClient, instructions: AgentInstructions.For(role));
    }
}
