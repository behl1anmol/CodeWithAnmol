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
public sealed class AgentFrameworkAgentFactory : IAgentFactory
{
    private readonly IChatModelProvider _modelProvider;
    private readonly IChatClientFactory _chatClientFactory;

    public AgentFrameworkAgentFactory(
        IChatModelProvider modelProvider,
        IChatClientFactory chatClientFactory)
    {
        _modelProvider = modelProvider;
        _chatClientFactory = chatClientFactory;
    }

    public AIAgent CreateAgent(AgentRole role)
    {
        ChatModelDescriptor descriptor = _modelProvider.Describe(role);
        IChatClient chatClient = _chatClientFactory.Create(descriptor);

        // Verified pattern (docs §3.1 / §4): IChatClient -> AIAgent.
        return new ChatClientAgent(chatClient, instructions: AgentInstructions.For(role));
    }
}
