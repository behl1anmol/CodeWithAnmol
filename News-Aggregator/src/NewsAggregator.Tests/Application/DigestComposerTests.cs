using NewsAggregator.Core.Application.Editorial;
using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Application;

/// <summary>
/// P3: <see cref="DigestComposer"/> is the pure, deterministic editorial structure — sort
/// by relevance desc (stable title tie-break), group by category, sections ordered by their
/// top item's score. Tested with no agent, since this is exactly the part that must be
/// deterministic (docs §3.4).
/// </summary>
public sealed class DigestComposerTests
{
    private static EnrichedItem Item(string title, string category, double score)
        => new()
        {
            Item = new NewsItem
            {
                Id = title,
                Title = title,
                Url = new Uri($"https://example.com/{Uri.EscapeDataString(title)}"),
                Source = "HackerNews",
                Content = $"Body of {title}.",
            },
            Summary = $"Summary of {title}.",
            Category = category,
            Relevance = new RelevanceScore(score),
        };

    [Fact]
    public void Empty_input_yields_no_sections()
    {
        IReadOnlyList<DigestSection> sections = DigestComposer.Compose([]);

        Assert.Empty(sections);
    }

    [Fact]
    public void Items_within_a_section_are_ordered_by_score_descending()
    {
        IReadOnlyList<EnrichedItem> items =
        [
            Item("low", "AI", 0.2),
            Item("high", "AI", 0.9),
            Item("mid", "AI", 0.5),
        ];

        DigestSection section = Assert.Single(DigestComposer.Compose(items));

        Assert.Equal("AI", section.Category);
        Assert.Equal(["high", "mid", "low"], section.Items.Select(i => i.Item.Title));
    }

    [Fact]
    public void Equal_scores_tie_break_on_title_ordinal_deterministically()
    {
        // Same score, supplied out of title order: the stable ordinal tie-break must put
        // "alpha" before "beta" regardless of input order.
        IReadOnlyList<EnrichedItem> items =
        [
            Item("beta", "AI", 0.5),
            Item("alpha", "AI", 0.5),
        ];

        DigestSection section = Assert.Single(DigestComposer.Compose(items));

        Assert.Equal(["alpha", "beta"], section.Items.Select(i => i.Item.Title));
    }

    [Fact]
    public void Sections_are_ordered_by_their_top_items_score()
    {
        IReadOnlyList<EnrichedItem> items =
        [
            Item("ai-weak", "AI", 0.3),
            Item("sec-strong", "Security", 0.95),
            Item("cloud-mid", "Cloud", 0.6),
        ];

        IReadOnlyList<DigestSection> sections = DigestComposer.Compose(items);

        // Security (0.95) > Cloud (0.6) > AI (0.3).
        Assert.Equal(["Security", "Cloud", "AI"], sections.Select(s => s.Category));
    }

    [Fact]
    public void Groups_items_by_category()
    {
        IReadOnlyList<EnrichedItem> items =
        [
            Item("a1", "AI", 0.8),
            Item("s1", "Security", 0.7),
            Item("a2", "AI", 0.6),
            Item("s2", "Security", 0.5),
        ];

        IReadOnlyList<DigestSection> sections = DigestComposer.Compose(items);

        Assert.Equal(2, sections.Count);
        DigestSection ai = Assert.Single(sections, s => s.Category == "AI");
        DigestSection security = Assert.Single(sections, s => s.Category == "Security");
        Assert.Equal(["a1", "a2"], ai.Items.Select(i => i.Item.Title));
        Assert.Equal(["s1", "s2"], security.Items.Select(i => i.Item.Title));
    }

    [Fact]
    public void Composer_leaves_intro_null_for_the_editor_to_fill()
    {
        IReadOnlyList<DigestSection> sections = DigestComposer.Compose([Item("x", "AI", 0.5)]);

        Assert.Null(Assert.Single(sections).Intro);
    }
}
