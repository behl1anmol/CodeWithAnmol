namespace NewsAggregator.Core.Configuration;

/// <summary>Bound from the "Sources" configuration section. Plain POCO — no
/// framework attributes — so it can live in the framework-free Core.</summary>
public sealed class SourceOptions
{
    public const string SectionName = "Sources";

    public HackerNewsOptions HackerNews { get; set; } = new();

    public RssOptions Rss { get; set; } = new();

    public GitHubOptions GitHub { get; set; } = new();
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

    /// <summary>Maximum items mapped from a single feed (most-recent first as the feed orders them).</summary>
    public int MaxItemsPerFeed { get; set; } = 20;

    /// <summary>Per-feed deadline covering the HTTP fetch and parse. Non-positive disables the timeout.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Upper bound on feeds fetched concurrently.</summary>
    public int MaxConcurrency { get; set; } = 4;
}

public sealed class GitHubOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Repositories to pull releases from, each as "owner/repo" (e.g. "dotnet/runtime").</summary>
    public IList<string> Repositories { get; set; } = [];

    /// <summary>Maximum releases mapped from a single repository (most-recent first as GitHub orders them).</summary>
    public int MaxReleasesPerRepo { get; set; } = 5;

    /// <summary>Per-repository deadline covering the HTTP fetch and parse. Non-positive disables the timeout.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Upper bound on repositories fetched concurrently.</summary>
    public int MaxConcurrency { get; set; } = 4;
}
