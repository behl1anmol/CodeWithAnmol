namespace NewsAggregator.Core.Application.Enrichment;

/// <summary>
/// The fixed category taxonomy for enriched items (docs §1.4 / §3.2). Lives in Core
/// so the enrichment assembler and (later) the UI category filter share one source of
/// truth. BCL-only — no framework coupling.
/// </summary>
public static class Taxonomy
{
    /// <summary>Fallback bucket for blank or unrecognised categories.</summary>
    public const string Other = "Other";

    /// <summary>
    /// The closed set of allowed categories. <see cref="Other"/> is last so it reads as
    /// the catch-all. Order is also the natural display order for the UI filter.
    /// </summary>
    public static IReadOnlyList<string> Categories { get; } =
    [
        "AI",
        "Security",
        "Cloud",
        "Devtools",
        "Web",
        "Data",
        "Hardware",
        Other,
    ];

    /// <summary>
    /// Maps an arbitrary (LLM-produced) category onto the taxonomy: a case-insensitive
    /// match returns the canonical casing; anything blank or unknown collapses to
    /// <see cref="Other"/>. Total — never throws.
    /// </summary>
    public static string Normalize(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Other;
        }

        string trimmed = category.Trim();
        foreach (string known in Categories)
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return Other;
    }
}
