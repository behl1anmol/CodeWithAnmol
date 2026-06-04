namespace NewsAggregator.Core.Domain;

/// <summary>
/// Identifies which model provider backs the aggregator. Provider-neutral so it
/// can live in the framework-free Core.
/// </summary>
public enum ModelProvider
{
    Ollama,
    OpenRouter,
}

/// <summary>
/// A provider-neutral description of the chat model to use for a given agent
/// role. The Infrastructure layer translates this into a concrete
/// <c>IChatClient</c> (Microsoft.Extensions.AI) — that SDK type never appears
/// in Core.
/// </summary>
public sealed record ChatModelDescriptor
{
    public required ModelProvider Provider { get; init; }

    public required string ModelId { get; init; }

    public required Uri Endpoint { get; init; }
}
