using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Editorial;

/// <summary>
/// Pure, framework-free editorial structuring (docs §3.4): turns enriched items into
/// ordered, category-grouped <see cref="DigestSection"/>s with a fully deterministic
/// shape. No LLM, no <c>Intro</c> — the Editor agent fills section intros afterwards.
/// Keeping this in Core means the ordering/grouping contract is unit-tested without any
/// agent, so the workflow's only non-deterministic part is the prose the Editor writes.
/// </summary>
public static class DigestComposer
{
    /// <summary>
    /// Orders <paramref name="items"/> by relevance (descending) with a stable
    /// <see cref="NewsItem.Title"/> ordinal tie-break, then groups them by category. A
    /// global descending sort followed by a first-appearance grouping makes each section
    /// appear in the order of its top item's score, with section items in the same sorted
    /// order. Total — empty input yields no sections.
    /// </summary>
    public static IReadOnlyList<DigestSection> Compose(IReadOnlyList<EnrichedItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return
        [
            .. items
                // OrderBy/ThenBy are stable, so equal-score equal-title items keep input
                // order — the structure is a deterministic function of the input list.
                .OrderByDescending(item => item.Relevance.Value)
                .ThenBy(item => item.Item.Title, StringComparer.Ordinal)
                // GroupBy preserves first-appearance order of groups and within-group order,
                // so the highest-scoring item of each category anchors its section's rank.
                // Group case-insensitively so all three category comparers agree — this one,
                // EditorIntroParser's intro map, and the workflow's intro lookup all use
                // OrdinalIgnoreCase. Categories already arrive canonical from the Categorizer
                // (Taxonomy.Normalize), so this changes nothing today; it just prevents a
                // hypothetical "AI"/"ai" split from producing a section the intro map (keyed
                // OrdinalIgnoreCase) could never match. group.Key takes the first item's
                // (canonical) casing.
                .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DigestSection
                {
                    Category = group.Key,
                    Items = [.. group],
                }),
        ];
    }
}
