using NewsAggregator.Core.Application.Enrichment;
using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Application;

/// <summary>
/// The P1 contract: <see cref="EnrichedItemAssembler"/> turns raw agent replies into a
/// valid <see cref="EnrichedItem"/> and is <b>total</b> — no LLM output makes it throw.
/// Every test asserts a valid item is produced.
/// </summary>
public sealed class EnrichedItemAssemblerTests
{
    private static NewsItem Item(string? content = "Article body content.") => new()
    {
        Id = "1",
        Title = "Some Title",
        Url = new Uri("https://example.com/a"),
        Source = "HackerNews",
        Content = content,
    };

    [Fact]
    public void Maps_clean_json_to_summary_category_tags_and_score()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(),
            "A neutral two sentence summary. It is factual.",
            "{\"category\":\"AI\",\"tags\":[\"llm\",\"agents\"]}",
            "{\"score\":0.8,\"reason\":\"notable release\"}");

        Assert.Equal("A neutral two sentence summary. It is factual.", result.Summary);
        Assert.Equal("AI", result.Category);
        Assert.Equal(["llm", "agents"], result.Tags);
        Assert.Equal(0.8, result.Relevance.Value);
        Assert.Equal("notable release", result.Relevance.Reason);
    }

    [Fact]
    public void Parses_json_wrapped_in_markdown_code_fences()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(),
            "summary",
            "```json\n{\"category\":\"Security\",\"tags\":[\"cve\"]}\n```",
            "```\n{\"score\":0.5}\n```");

        Assert.Equal("Security", result.Category);
        Assert.Equal(["cve"], result.Tags);
        Assert.Equal(0.5, result.Relevance.Value);
    }

    [Fact]
    public void Parses_json_surrounded_by_prose()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(),
            "summary",
            "Sure! Here is the classification: {\"category\":\"Cloud\",\"tags\":[\"aws\"]} Hope that helps.",
            "I'd rate it {\"score\":0.3,\"reason\":\"incremental\"} overall.");

        Assert.Equal("Cloud", result.Category);
        Assert.Equal(["aws"], result.Tags);
        Assert.Equal(0.3, result.Relevance.Value);
        Assert.Equal("incremental", result.Relevance.Reason);
    }

    [Fact]
    public void Tolerates_braces_inside_string_values()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(),
            "summary",
            "{\"category\":\"Devtools\",\"tags\":[\"regex {curly}\"]}",
            "{\"score\":0.2,\"reason\":\"uses {placeholders}\"}");

        Assert.Equal("Devtools", result.Category);
        Assert.Equal(["regex {curly}"], result.Tags);
        Assert.Equal("uses {placeholders}", result.Relevance.Reason);
    }

    [Fact]
    public void Missing_category_falls_back_to_other()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(), "summary", "{\"tags\":[\"x\"]}", "{\"score\":0.1}");

        Assert.Equal(Taxonomy.Other, result.Category);
    }

    [Fact]
    public void Non_taxonomy_category_falls_back_to_other()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(), "summary", "{\"category\":\"Politics\",\"tags\":[]}", "{\"score\":0.1}");

        Assert.Equal(Taxonomy.Other, result.Category);
    }

    [Theory]
    [InlineData("ai", "AI")]
    [InlineData("SECURITY", "Security")]
    [InlineData("  cloud  ", "Cloud")]
    public void Category_match_is_case_insensitive_and_canonicalized(string raw, string expected)
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(), "summary", $"{{\"category\":\"{raw.Trim()}\"}}", "{\"score\":0.1}");

        Assert.Equal(expected, result.Category);
    }

    [Fact]
    public void Missing_score_yields_zero_relevance()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(), "summary", "{\"category\":\"AI\"}", "{\"reason\":\"no number\"}");

        Assert.Equal(RelevanceScore.Zero, result.Relevance);
        Assert.Equal(0.0, result.Relevance.Value);
    }

    [Theory]
    [InlineData("{\"score\":1.5}")]
    [InlineData("{\"score\":-0.5}")]
    [InlineData("{\"score\":\"not-a-number\"}")]
    [InlineData("totally not json")]
    [InlineData("")]
    [InlineData(null)]
    public void Out_of_range_or_unparseable_score_yields_zero_relevance(string? rankerJson)
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(), "summary", "{\"category\":\"AI\"}", rankerJson);

        Assert.Equal(0.0, result.Relevance.Value);
        Assert.Null(result.Relevance.Reason);
    }

    [Fact]
    public void Boundary_scores_zero_and_one_are_accepted()
    {
        EnrichedItem low = EnrichedItemAssembler.Assemble(
            Item(), "summary", "{\"category\":\"AI\"}", "{\"score\":0.0}");
        EnrichedItem high = EnrichedItemAssembler.Assemble(
            Item(), "summary", "{\"category\":\"AI\"}", "{\"score\":1.0}");

        Assert.Equal(0.0, low.Relevance.Value);
        Assert.Equal(1.0, high.Relevance.Value);
    }

    [Fact]
    public void Blank_summary_falls_back_to_content_snippet()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(content: "The real article body."),
            "   ",
            "{\"category\":\"AI\"}",
            "{\"score\":0.1}");

        Assert.Equal("The real article body.", result.Summary);
    }

    [Fact]
    public void Blank_summary_and_content_falls_back_to_title()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(content: null), null, "{\"category\":\"AI\"}", "{\"score\":0.1}");

        Assert.Equal("Some Title", result.Summary);
    }

    [Fact]
    public void Long_content_snippet_is_capped()
    {
        string longBody = new('x', 1000);

        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(content: longBody), null, "{\"category\":\"AI\"}", "{\"score\":0.1}");

        Assert.Equal(500, result.Summary.Length);
    }

    [Fact]
    public void Tags_are_trimmed_blank_dropped_deduped_and_capped_at_five()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(),
            "summary",
            "{\"category\":\"AI\",\"tags\":[\" llm \",\"\",\"   \",\"LLM\",\"a\",\"b\",\"c\",\"d\",\"e\"]}",
            "{\"score\":0.1}");

        // " llm " trimmed to "llm"; "LLM" is a case-insensitive duplicate; blanks dropped;
        // result capped at five entries.
        Assert.Equal(["llm", "a", "b", "c", "d"], result.Tags);
    }

    [Fact]
    public void Garbage_everywhere_still_produces_a_valid_item()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(content: null), null, "not json at all", "also not json");

        // No throw; invariants satisfied via fallbacks.
        Assert.Equal("Some Title", result.Summary);
        Assert.Equal(Taxonomy.Other, result.Category);
        Assert.Empty(result.Tags);
        Assert.Equal(0.0, result.Relevance.Value);
    }

    [Fact]
    public void Null_item_throws_argument_null()
        => Assert.Throws<ArgumentNullException>(
            () => EnrichedItemAssembler.Assemble(null!, "s", "{}", "{}"));

    [Fact]
    public void Blank_relevance_reason_becomes_null()
    {
        EnrichedItem result = EnrichedItemAssembler.Assemble(
            Item(), "summary", "{\"category\":\"AI\"}", "{\"score\":0.4,\"reason\":\"   \"}");

        Assert.Equal(0.4, result.Relevance.Value);
        Assert.Null(result.Relevance.Reason);
    }
}
