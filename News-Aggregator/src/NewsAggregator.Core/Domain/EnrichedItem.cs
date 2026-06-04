namespace NewsAggregator.Core.Domain;

/// <summary>
/// A <see cref="NewsItem"/> after the concurrent enrichment workflow has added
/// a summary, a category + tags, and a relevance score.
/// </summary>
public sealed record EnrichedItem
{
    public required NewsItem Item
    {
        get => field;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <summary>Neutral 2-3 sentence summary from the Summarizer agent.</summary>
    public required string Summary
    {
        get => field;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>Single category from the taxonomy (e.g. "AI", "Security").</summary>
    public required string Category
    {
        get => field;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public IReadOnlyList<string> Tags
    {
        get => field;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("Tags must not contain null or blank entries.", nameof(value));
            }
            field = value;
        }
    } = [];

    public RelevanceScore Relevance { get; init; } = RelevanceScore.Zero;
}
