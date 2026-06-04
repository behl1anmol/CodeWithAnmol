namespace NewsAggregator.Core.Configuration;

/// <summary>Bound from the "Sources" configuration section. Plain POCO — no
/// framework attributes — so it can live in the framework-free Core.</summary>
public sealed class SourceOptions
{
    public const string SectionName = "Sources";

    public HackerNewsOptions HackerNews { get; set; } = new();

    public RssOptions Rss { get; set; } = new();
}

public sealed class HackerNewsOptions
{
    public bool Enabled { get; set; } = true;

    public int MaxItems { get; set; } = 30;

    /// <summary>HN list endpoint, e.g. "topstories" | "newstories" | "beststories".</summary>
    public string Story { get; set; } = "topstories";
}

public sealed class RssOptions
{
    public bool Enabled { get; set; } = true;

    public IList<string> Feeds { get; set; } = [];
}
