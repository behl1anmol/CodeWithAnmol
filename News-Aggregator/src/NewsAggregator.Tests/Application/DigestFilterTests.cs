using NewsAggregator.Core.Application.Editorial;
using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Application;

/// <summary>
/// P4: <see cref="DigestFilter"/> is the pure presentation filter behind the Blazor digest
/// page — narrow by category and/or tag (case-insensitive AND), drop emptied sections, and
/// list distinct tags for the picker. Tested with no UI, since all filtering logic lives in
/// Core (no business logic in the component).
/// </summary>
public sealed class DigestFilterTests
{
    private static readonly DateTimeOffset When = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private static EnrichedItem Item(string title, string category, double score, params string[] tags)
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
            Tags = tags,
            Relevance = new RelevanceScore(score),
        };

    private static Digest MakeDigest(params EnrichedItem[] items)
        => new()
        {
            GeneratedAt = When,
            Sections = DigestComposer.Compose(items),
        };

    [Fact]
    public void No_filter_returns_the_digest_unchanged()
    {
        Digest digest = MakeDigest(Item("a", "AI", 0.8), Item("s", "Security", 0.7));

        Digest result = DigestFilter.Apply(digest);

        Assert.Same(digest, result);
    }

    [Fact]
    public void Blank_filters_are_treated_as_no_constraint()
    {
        Digest digest = MakeDigest(Item("a", "AI", 0.8));

        Digest result = DigestFilter.Apply(digest, "   ", "  ");

        Assert.Same(digest, result);
    }

    [Fact]
    public void Filters_by_category_keeping_only_the_matching_section()
    {
        Digest digest = MakeDigest(
            Item("a", "AI", 0.8),
            Item("s", "Security", 0.7),
            Item("c", "Cloud", 0.6));

        Digest result = DigestFilter.Apply(digest, category: "Security");

        DigestSection section = Assert.Single(result.Sections);
        Assert.Equal("Security", section.Category);
        Assert.Equal(["s"], section.Items.Select(i => i.Item.Title));
    }

    [Fact]
    public void Category_match_is_case_insensitive()
    {
        Digest digest = MakeDigest(Item("a", "AI", 0.8), Item("s", "Security", 0.7));

        Digest result = DigestFilter.Apply(digest, category: "ai");

        Assert.Equal("AI", Assert.Single(result.Sections).Category);
    }

    [Fact]
    public void Filters_by_tag_dropping_items_and_emptied_sections()
    {
        Digest digest = MakeDigest(
            Item("a1", "AI", 0.9, "llm", "agents"),
            Item("a2", "AI", 0.8, "hardware"),
            Item("s1", "Security", 0.7, "cve"));

        Digest result = DigestFilter.Apply(digest, tag: "llm");

        DigestSection section = Assert.Single(result.Sections);
        Assert.Equal("AI", section.Category);
        Assert.Equal(["a1"], section.Items.Select(i => i.Item.Title));
    }

    [Fact]
    public void Tag_match_is_case_insensitive()
    {
        Digest digest = MakeDigest(Item("a1", "AI", 0.9, "LLM"));

        Digest result = DigestFilter.Apply(digest, tag: "llm");

        Assert.Single(Assert.Single(result.Sections).Items);
    }

    [Fact]
    public void Combines_category_and_tag_with_logical_and()
    {
        Digest digest = MakeDigest(
            Item("a-llm", "AI", 0.9, "llm"),
            Item("a-hw", "AI", 0.8, "hardware"),
            Item("s-llm", "Security", 0.7, "llm"));

        Digest result = DigestFilter.Apply(digest, category: "AI", tag: "llm");

        DigestSection section = Assert.Single(result.Sections);
        Assert.Equal("AI", section.Category);
        Assert.Equal(["a-llm"], section.Items.Select(i => i.Item.Title));
    }

    [Fact]
    public void Unmatched_filter_yields_empty_sections_but_preserves_timestamp()
    {
        Digest digest = MakeDigest(Item("a", "AI", 0.8));

        Digest result = DigestFilter.Apply(digest, category: "Hardware");

        Assert.Empty(result.Sections);
        Assert.Equal(When, result.GeneratedAt);
    }

    [Fact]
    public void DistinctTags_dedupes_case_insensitively_and_orders()
    {
        Digest digest = MakeDigest(
            Item("a", "AI", 0.9, "llm", "agents"),
            Item("s", "Security", 0.7, "LLM", "cve"));

        IReadOnlyList<string> tags = DigestFilter.DistinctTags(digest);

        // "llm"/"LLM" collapse to the first seen; result is ordinal-ordered.
        Assert.Equal(["agents", "cve", "llm"], tags);
    }

    [Fact]
    public void DistinctTags_on_empty_digest_is_empty()
    {
        Digest digest = new() { GeneratedAt = When };

        Assert.Empty(DigestFilter.DistinctTags(digest));
    }
}
