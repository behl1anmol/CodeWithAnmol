namespace NewsAggregator.Core.Configuration;

/// <summary>Bound from the "Cache" configuration section.</summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>"Memory" (MVP default) or "Redis" (post-MVP / optional).</summary>
    public string Provider { get; set; } = "Memory";
}
