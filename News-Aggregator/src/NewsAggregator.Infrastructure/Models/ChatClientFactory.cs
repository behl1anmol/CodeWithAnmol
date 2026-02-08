using Microsoft.Extensions.AI;
using NewsAggregator.Core.Domain;
using OllamaSharp;

namespace NewsAggregator.Infrastructure.Models;

/// <summary>
/// Builds a wrapped <see cref="IChatClient"/> for the requested provider.
///
/// Verified integration points (docs §4):
///   • Ollama (local):   OllamaSharp.OllamaApiClient implements IChatClient (v4+/v5).
///   • Pipeline:          AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build(sp).
///
/// OpenRouter (BYOK) support is added in a later episode.
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ChatClientFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IChatClient Create(ChatModelDescriptor descriptor)
    {
        IChatClient raw = descriptor.Provider switch
        {
            ModelProvider.Ollama => CreateOllama(descriptor),
            _ => throw new ArgumentOutOfRangeException(
                nameof(descriptor), descriptor.Provider, "Unknown model provider."),
        };

        // Cross-cutting pipeline applied uniformly to every provider.
        return raw
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry()
            // .UseDistributedCache()  // TODO(post-MVP): enable with Redis from Aspire.
            .Build(_serviceProvider);
    }

    private static IChatClient CreateOllama(ChatModelDescriptor descriptor)
        => new OllamaApiClient(descriptor.Endpoint, descriptor.ModelId);
}
