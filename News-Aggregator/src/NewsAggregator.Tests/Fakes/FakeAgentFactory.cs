using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Agents;

namespace NewsAggregator.Tests.Fakes;

/// <summary>
/// Deterministic <see cref="IAgentFactory"/> that backs each role with a canned reply, so
/// the concurrent/sequential workflows can be unit-tested without a live model. Agents are
/// left unnamed (mirroring <c>AgentFrameworkAgentFactory</c>), so each carries a unique
/// <see cref="AIAgent.Id"/> the workflow can route updates by, and they are <b>cached
/// one-per-role</b> exactly like the real factory — so tests exercise the same reuse path
/// (the same agent instance shared across concurrent per-article runs).
/// </summary>
/// <remarks>
/// The optional <c>clientFactory</c> lets a test substitute the backing
/// <see cref="IChatClient"/> (e.g. to observe concurrency or inject delays) while keeping
/// the per-role canned replies.
/// </remarks>
public sealed class FakeAgentFactory : IAgentFactory
{
    private readonly Dictionary<AgentRole, string> _replies;
    private readonly Func<AgentRole, string, IChatClient> _clientFactory;
    private readonly ConcurrentDictionary<AgentRole, Lazy<AIAgent>> _agents = new();

    public FakeAgentFactory(
        string summary,
        string categorizerJson,
        string rankerJson,
        string? editor = null,
        Func<AgentRole, string, IChatClient>? clientFactory = null)
    {
        _replies = new Dictionary<AgentRole, string>
        {
            [AgentRole.Summarizer] = summary,
            [AgentRole.Categorizer] = categorizerJson,
            [AgentRole.Ranker] = rankerJson,
        };
        if (editor is not null)
        {
            _replies[AgentRole.Editor] = editor;
        }

        _clientFactory = clientFactory ?? ((_, reply) => new FakeChatClient(reply));
    }

    public AIAgent CreateAgent(AgentRole role)
        => _agents.GetOrAdd(
            role,
            r => new Lazy<AIAgent>(() => BuildAgent(r), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private AIAgent BuildAgent(AgentRole role)
    {
        string reply = _replies.TryGetValue(role, out string? value) ? value : string.Empty;
        return new ChatClientAgent(_clientFactory(role, reply), instructions: role.ToString());
    }
}
