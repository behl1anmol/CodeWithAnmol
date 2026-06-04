using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Configuration;

/// <summary>
/// Bound from the "Models" configuration section. Selects the active provider
/// and carries per-provider settings plus optional per-agent model overrides.
/// </summary>
public sealed class ModelOptions
{
    public const string SectionName = "Models";

    public ModelProvider Provider { get; set; } = ModelProvider.Ollama;

    public OllamaOptions Ollama { get; set; } = new();

    public OpenRouterOptions OpenRouter { get; set; } = new();

    public AgentModelOptions AgentModels { get; set; } = new();
}

public sealed class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";

    public string DefaultModel { get; set; } = "llama3.2";
}

public sealed class OpenRouterOptions
{
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";

    public string DefaultModel { get; set; } = "openai/gpt-4o-mini";

    /// <summary>
    /// BYOK secret. NEVER committed — supplied via user-secrets / env var /
    /// Aspire parameter. Required only when <see cref="ModelOptions.Provider"/>
    /// is <see cref="ModelProvider.OpenRouter"/> (validated at startup).
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>Optional per-agent model id overrides; null = use provider default.</summary>
public sealed class AgentModelOptions
{
    public string? Summarizer { get; set; }

    public string? Categorizer { get; set; }

    public string? Ranker { get; set; }

    public string? Editor { get; set; }
}
