using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Models;

/// <summary>
/// Resolves the descriptor for the local Ollama provider, honoring per-agent
/// model overrides and falling back to the configured default model.
/// </summary>
public sealed class OllamaChatModelProvider : IChatModelProvider
{
    private readonly ModelOptions _options;

    public OllamaChatModelProvider(IOptions<ModelOptions> options)
    {
        _options = options.Value;
    }

    public ChatModelDescriptor Describe(AgentRole role) => new()
    {
        Provider = ModelProvider.Ollama,
        ModelId = ResolveModel(role),
        Endpoint = new Uri(_options.Ollama.Endpoint),
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

        return string.IsNullOrWhiteSpace(overrideModel) ? _options.Ollama.DefaultModel : overrideModel;
    }
}
