using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using OpenAI;
using OllamaSharp;

namespace NewsAggregator.Infrastructure.Models;

/// <summary>
/// Builds a wrapped <see cref="IChatClient"/> for the requested provider.
///
/// Verified integration points (docs §4):
///   • Ollama (local):   OllamaSharp.OllamaApiClient implements IChatClient (v4+/v5).
///   • OpenRouter (BYOK): OpenAIClient + AsIChatClient() over the OpenAI-compatible API.
///   • Pipeline:          AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build(sp).
/// </summary>
public sealed class ChatClientFactory : IChatClientFactory
{
    private readonly ModelOptions _options;
    private readonly IServiceProvider _serviceProvider;

    public ChatClientFactory(IOptions<ModelOptions> options, IServiceProvider serviceProvider)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
    }

    public IChatClient Create(ChatModelDescriptor descriptor)
    {
        IChatClient raw = descriptor.Provider switch
        {
            ModelProvider.Ollama => CreateOllama(descriptor),
            ModelProvider.OpenRouter => CreateOpenRouter(descriptor),
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

    private IChatClient CreateOpenRouter(ChatModelDescriptor descriptor)
    {
        string apiKey = _options.OpenRouter.ApiKey
            ?? throw new InvalidOperationException(
                "Models:OpenRouter:ApiKey is required when the provider is OpenRouter.");

        // ⚠️ Endpoint-override option name can vary by OpenAI SDK version — verified
        // against OpenAI 2.10.0 at build time. Fallback: a custom HttpClient transport
        // whose BaseAddress points at OpenRouter (docs §4.4).
        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = descriptor.Endpoint });

        return client.GetChatClient(descriptor.ModelId).AsIChatClient();
    }
}
