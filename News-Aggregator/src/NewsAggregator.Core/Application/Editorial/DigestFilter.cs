using NewsAggregator.Core.Application.Enrichment;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Editorial;

/// <summary>
/// Pure, framework-free presentation filter (P4): narrows a <see cref="Digest"/> to a
/// single category and/or a single tag for the UI, and lists the categories/tags available
/// for the pickers. Kept in Core next to <see cref="DigestComposer"/> so the filtering
/// contract is unit-tested without any UI — the Blazor page only binds inputs and renders.
/// </summary>
public static class DigestFilter
{
    /// <summary>
    /// Returns a digest containing only the sections/items that match both
    /// <paramref name="category"/> and <paramref name="tag"/> (logical AND). A null or blank
    /// argument means "no constraint on that dimension"; with neither set the original digest
    /// is returned unchanged. Matching is case-insensitive. Sections left empty by a tag
    /// filter are dropped. <see cref="Digest.GeneratedAt"/> is always preserved, so the
    /// result is valid even when nothing matches (empty sections).
    /// </summary>
    public static Digest Apply(Digest digest, string? category = null, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(digest);

        bool hasCategory = !string.IsNullOrWhiteSpace(category);
        bool hasTag = !string.IsNullOrWhiteSpace(tag);
        if (!hasCategory && !hasTag)
        {
            return digest;
        }

        IEnumerable<DigestSection> sections = digest.Sections;

        if (hasCategory)
        {
            sections = sections.Where(
                section => string.Equals(section.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (hasTag)
        {
            sections = sections
                .Select(section => section with
                {
                    Items =
                    [
                        .. section.Items.Where(
                            item => item.Tags.Any(
                                t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))),
                    ],
                })
                .Where(section => section.Items.Count > 0);
        }

        return new Digest
        {
            GeneratedAt = digest.GeneratedAt,
            Sections = [.. sections],
        };
    }

    /// <summary>
    /// The taxonomy categories actually present in the digest, in taxonomy display order, so
    /// the category picker never offers a category that would render an empty result.
    /// </summary>
    public static IReadOnlyList<string> PresentCategories(Digest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        return
        [
            .. Taxonomy.Categories.Where(
                category => digest.Sections.Any(
                    section => string.Equals(section.Category, category, StringComparison.OrdinalIgnoreCase))),
        ];
    }

    /// <summary>
    /// The distinct tags across every item in the digest, de-duplicated and ordered
    /// case-insensitively, for populating a tag picker. Empty digest yields an empty list.
    /// </summary>
    public static IReadOnlyList<string> DistinctTags(Digest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        return
        [
            .. digest.Sections
                .SelectMany(section => section.Items)
                .SelectMany(item => item.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase),
        ];
    }
}
