using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Domain;

/// <summary>
/// Construction invariants and defaults of <see cref="EnrichedItem"/>.
/// </summary>
public sealed class EnrichedItemTests
{
    private static NewsItem SampleItem()
        => new() { Id = "1", Title = "t", Url = new Uri("https://example.com"), Source = "src" };

    private static EnrichedItem Valid() => new()
    {
        Item = SampleItem(),
        Summary = "s",
        Category = "AI",
    };

    [Fact]
    public void Constructs_with_valid_values()
    {
        EnrichedItem enriched = Valid();

        Assert.Equal("AI", enriched.Category);
        Assert.Equal("s", enriched.Summary);
    }

    [Fact]
    public void Defaults_tags_empty_and_relevance_zero()
    {
        EnrichedItem enriched = Valid();

        Assert.Empty(enriched.Tags);
        Assert.Equal(RelevanceScore.Zero, enriched.Relevance);
    }

    [Fact]
    public void Rejects_null_item()
        => Assert.Throws<ArgumentNullException>(() => Valid() with { Item = null! });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_summary(string? summary)
        => Assert.ThrowsAny<ArgumentException>(() => Valid() with { Summary = summary! });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_category(string? category)
        => Assert.ThrowsAny<ArgumentException>(() => Valid() with { Category = category! });

    [Fact]
    public void Rejects_null_tags()
        => Assert.Throws<ArgumentNullException>(() => Valid() with { Tags = null! });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_tag_entry_that_is_blank(string? tag)
        => Assert.Throws<ArgumentException>(() => Valid() with { Tags = ["ok", tag!] });

    [Fact]
    public void Allows_valid_tags()
    {
        EnrichedItem enriched = Valid() with { Tags = ["ml", "llm"] };

        Assert.Equal(["ml", "llm"], enriched.Tags);
    }
}
