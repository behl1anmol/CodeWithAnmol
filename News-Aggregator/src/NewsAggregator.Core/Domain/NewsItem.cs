namespace NewsAggregator.Core.Domain;

/// <summary>
/// A raw news item as collected from a source, before any LLM enrichment.
/// Immutable value type; carries no behavior beyond construction invariants.
/// </summary>
public sealed record NewsItem
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required Uri Url { get; init; }

    /// <summary>Name of the originating source (e.g. "HackerNews", "RSS").</summary>
    public required string Source { get; init; }

    /// <summary>Optional article body / excerpt when the source provides one.</summary>
    public string? Content { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}
