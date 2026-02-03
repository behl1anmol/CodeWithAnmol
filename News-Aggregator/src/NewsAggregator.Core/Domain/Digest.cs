namespace NewsAggregator.Core.Domain;

/// <summary>
/// The final, ordered, categorized digest produced by the sequential editorial
/// workflow and rendered by the UI.
/// </summary>
public sealed record Digest
{
    public required DateTimeOffset GeneratedAt
    {
        get => field;
        init
        {
            if (value == default)
            {
                throw new ArgumentException("Digest must have a generation timestamp.", nameof(value));
            }
            field = value;
        }
    }

    public IReadOnlyList<DigestSection> Sections
    {
        get => field;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Any(section => section is null))
            {
                throw new ArgumentException("Digest sections must not contain null entries.", nameof(value));
            }
            field = value;
        }
    } = [];
}

/// <summary>One category section of a <see cref="Digest"/>.</summary>
public sealed record DigestSection
{
    public required string Category
    {
        get => field;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>Optional editor-written intro for the section.</summary>
    public string? Intro { get; init; }

    public IReadOnlyList<EnrichedItem> Items
    {
        get => field;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Any(item => item is null))
            {
                throw new ArgumentException("Digest section items must not contain null entries.", nameof(value));
            }
            field = value;
        }
    } = [];
}
