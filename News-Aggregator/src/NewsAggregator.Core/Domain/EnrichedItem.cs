namespace NewsAggregator.Core.Domain;

/// <summary>
/// A <see cref="NewsItem"/> after the concurrent enrichment workflow has added
/// a summary, a category + tags, and a relevance score.
/// </summary>
public sealed record EnrichedItem
{
    public required NewsItem Item { get; init; }

    /// <summary>Neutral 2-3 sentence summary from the Summarizer agent.</summary>
    public required string Summary { get; init; }

    /// <summary>Single category from the taxonomy (e.g. "AI", "Security").</summary>
    public required string Category { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public RelevanceScore Relevance { get; init; } = RelevanceScore.Zero;
}
