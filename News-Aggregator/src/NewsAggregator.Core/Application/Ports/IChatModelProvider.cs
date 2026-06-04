using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Ports;

/// <summary>
/// Resolves which model (provider + model id + endpoint) backs a given agent
/// role, based on configuration. Returns the provider-neutral
/// <see cref="ChatModelDescriptor"/>; building the actual chat client is an
/// Infrastructure concern.
/// </summary>
public interface IChatModelProvider
{
    ChatModelDescriptor Describe(AgentRole role);
}
