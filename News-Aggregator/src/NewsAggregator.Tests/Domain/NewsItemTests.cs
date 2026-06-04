using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Domain;

/// <summary>
/// Construction invariants of <see cref="NewsItem"/>: required strings must be
/// non-blank and the URL must be an absolute URI.
/// </summary>
public sealed class NewsItemTests
{
    private static NewsItem Valid() => new()
    {
        Id = "1",
        Title = "t",
        Url = new Uri("https://example.com/a"),
        Source = "src",
    };

    [Fact]
    public void Constructs_with_valid_values()
    {
        NewsItem item = Valid();

        Assert.Equal("1", item.Id);
        Assert.Equal(new Uri("https://example.com/a"), item.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_id(string? id)
        => Assert.ThrowsAny<ArgumentException>(() => Valid() with { Id = id! });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_title(string? title)
        => Assert.ThrowsAny<ArgumentException>(() => Valid() with { Title = title! });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_blank_source(string? source)
        => Assert.ThrowsAny<ArgumentException>(() => Valid() with { Source = source! });

    [Fact]
    public void Rejects_null_url()
        => Assert.Throws<ArgumentNullException>(() => Valid() with { Url = null! });

    [Fact]
    public void Rejects_relative_url()
        => Assert.Throws<ArgumentException>(
            () => Valid() with { Url = new Uri("/relative/path", UriKind.Relative) });

    [Fact]
    public void Allows_null_content_and_publish_date()
    {
        NewsItem item = Valid() with { Content = null, PublishedAt = null };

        Assert.Null(item.Content);
        Assert.Null(item.PublishedAt);
    }

    [Fact]
    public void With_expression_revalidates_changed_property()
        => Assert.ThrowsAny<ArgumentException>(() => Valid() with { Title = "" });
}
