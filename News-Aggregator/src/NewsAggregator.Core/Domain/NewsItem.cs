namespace NewsAggregator.Core.Domain;

/// <summary>
/// A raw news item as collected from a source, before any LLM enrichment.
/// Immutable value type; carries no behavior beyond construction invariants.
/// </summary>
public sealed record NewsItem
{
    public required string Id
    {
        get => field;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public required string Title
    {
        get => field;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    public required Uri Url
    {
        get => field;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!value.IsAbsoluteUri)
            {
                throw new ArgumentException("News item URL must be absolute.", nameof(value));
            }
            field = value;
        }
    }

    /// <summary>Name of the originating source (e.g. "HackerNews", "RSS").</summary>
    public required string Source
    {
        get => field;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>Optional article body / excerpt when the source provides one.</summary>
    public string? Content { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}
