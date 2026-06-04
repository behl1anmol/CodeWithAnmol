namespace NewsAggregator.Core.Domain;

/// <summary>
/// The final, ordered, categorized digest produced by the sequential editorial
/// workflow and rendered by the UI.
/// </summary>
public sealed record Digest
{
    public required DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<DigestSection> Sections { get; init; } = [];
}

/// <summary>One category section of a <see cref="Digest"/>.</summary>
public sealed record DigestSection
{
    public required string Category { get; init; }

    /// <summary>Optional editor-written intro for the section.</summary>
    public string? Intro { get; init; }

    public IReadOnlyList<EnrichedItem> Items { get; init; } = [];
}
