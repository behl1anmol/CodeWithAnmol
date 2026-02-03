using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Domain;

/// <summary>
/// Construction invariants of <see cref="Digest"/> and <see cref="DigestSection"/>.
/// </summary>
public sealed class DigestTests
{
    private static readonly DateTimeOffset When = DateTimeOffset.UnixEpoch;

    private static EnrichedItem SampleEnriched() => new()
    {
        Item = new NewsItem { Id = "1", Title = "t", Url = new Uri("https://example.com"), Source = "src" },
        Summary = "s",
        Category = "AI",
    };

    // ---- Digest -------------------------------------------------------------

    [Fact]
    public void Constructs_with_timestamp_and_empty_sections()
    {
        var digest = new Digest { GeneratedAt = When };

        Assert.Equal(When, digest.GeneratedAt);
        Assert.Empty(digest.Sections);
    }

    [Fact]
    public void Rejects_default_generated_at()
        => Assert.Throws<ArgumentException>(() => new Digest { GeneratedAt = default });

    [Fact]
    public void Rejects_null_sections()
        => Assert.Throws<ArgumentNullException>(() => new Digest { GeneratedAt = When, Sections = null! });

    [Fact]
    public void Rejects_sections_containing_null()
        => Assert.Throws<ArgumentException>(
            () => new Digest { GeneratedAt = When, Sections = [null!] });

    [Fact]
    public void Allows_populated_sections()
    {
        var digest = new Digest
        {
            GeneratedAt = When,
            Sections = [new DigestSection { Category = "AI", Items = [SampleEnriched()] }],
        };

        Assert.Single(digest.Sections);
    }

    // ---- DigestSection ------------------------------------------------------

    [Fact]
    public void Section_constructs_with_category_and_empty_items()
    {
        var section = new DigestSection { Category = "AI" };

        Assert.Equal("AI", section.Category);
        Assert.Empty(section.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Section_rejects_blank_category(string? category)
        => Assert.ThrowsAny<ArgumentException>(() => new DigestSection { Category = category! });

    [Fact]
    public void Section_rejects_items_containing_null()
        => Assert.Throws<ArgumentException>(
            () => new DigestSection { Category = "AI", Items = [null!] });
}
