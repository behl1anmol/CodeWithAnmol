using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Models;

/// <summary>
/// Resolves the descriptor for the hosted, BYOK OpenRouter provider
/// (OpenAI-compatible). The API key itself is read later by the chat-client
/// factory from <see cref="ModelOptions"/> — it never enters the Core domain.
/// </summary>
public sealed class OpenRouterChatModelProvider : IChatModelProvider
{
    private readonly ModelOptions _options;

    public OpenRouterChatModelProvider(IOptions<ModelOptions> options)
    {
        _options = options.Value;
    }

    public ChatModelDescriptor Describe(AgentRole role) => new()
    {
        Provider = ModelProvider.OpenRouter,
        ModelId = ResolveModel(role),
        Endpoint = new Uri(_options.OpenRouter.Endpoint),
    };

    private string ResolveModel(AgentRole role)
    {
        string? overrideModel = role switch
        {
            AgentRole.Summarizer => _options.AgentModels.Summarizer,
            AgentRole.Categorizer => _options.AgentModels.Categorizer,
            AgentRole.Ranker => _options.AgentModels.Ranker,
            AgentRole.Editor => _options.AgentModels.Editor,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(overrideModel) ? _options.OpenRouter.DefaultModel : overrideModel;
    }
}
