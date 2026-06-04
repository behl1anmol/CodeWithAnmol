namespace NewsAggregator.Core.Configuration;

/// <summary>Bound from the "Enrichment" configuration section.</summary>
public sealed class EnrichmentOptions
{
    public const string SectionName = "Enrichment";

    /// <summary>
    /// Upper bound on how many articles are enriched in parallel. Keeps local
    /// Ollama from being overwhelmed. The concurrent agent fan-out per article
    /// is separate (see docs §3.3).
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;
}
