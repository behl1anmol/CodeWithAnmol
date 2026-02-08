using Microsoft.Extensions.AI;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Models;

/// <summary>
/// Infrastructure-internal factory that turns a provider-neutral
/// <see cref="ChatModelDescriptor"/> into a fully-wrapped
/// <see cref="IChatClient"/> (Microsoft.Extensions.AI). This is the single place
/// the cross-cutting pipeline (function invocation, OpenTelemetry, ...) is
/// applied, so every provider behaves uniformly.
/// </summary>
public interface IChatClientFactory
{
    IChatClient Create(ChatModelDescriptor descriptor);
}
