using NewsAggregator.Core.Application.Editorial;
using Xunit;

namespace NewsAggregator.Tests.Application;

/// <summary>
/// The P3 contract: <see cref="EditorIntroParser"/> turns the Editor agent's reply into a
/// category → intro map and is <b>total</b> — fences, prose, braces inside strings, wrong
/// JSON shapes, or outright garbage never throw, they just yield fewer (or no) intros.
/// These tests pin the balanced-brace scanner and the per-entry filtering directly, rather
/// than only through the workflow happy path.
/// </summary>
public sealed class EditorIntroParserTests
{
    [Fact]
    public void Parses_clean_object_into_category_intro_map()
    {
        IReadOnlyDictionary<string, string> intros =
            EditorIntroParser.Parse("{\"AI\":\"AI intro.\",\"Security\":\"Sec intro.\"}");

        Assert.Equal(2, intros.Count);
        Assert.Equal("AI intro.", intros["AI"]);
        Assert.Equal("Sec intro.", intros["Security"]);
    }

    [Fact]
    public void Parses_object_wrapped_in_json_code_fence()
    {
        IReadOnlyDictionary<string, string> intros =
            EditorIntroParser.Parse("```json\n{\"AI\":\"AI intro.\"}\n```");

        Assert.Equal("AI intro.", intros["AI"]);
    }

    [Fact]
    public void Parses_object_surrounded_by_prose()
    {
        IReadOnlyDictionary<string, string> intros = EditorIntroParser.Parse(
            "Sure! Here are the intros: {\"AI\":\"AI intro.\"} Hope that helps.");

        Assert.Equal("AI intro.", intros["AI"]);
    }

    [Fact]
    public void Tolerates_braces_inside_string_values()
    {
        // The exact case the string-aware scanner exists for: a '{' inside a value must not
        // be treated as a nested object that ends brace matching early.
        IReadOnlyDictionary<string, string> intros = EditorIntroParser.Parse(
            "{\"AI\":\"models like {x} are common\",\"Security\":\"plain\"}");

        Assert.Equal("models like {x} are common", intros["AI"]);
        Assert.Equal("plain", intros["Security"]);
    }

    [Fact]
    public void Tolerates_escaped_quote_inside_string_value()
    {
        // The escape handling means an escaped quote does not close the string, so the
        // following ':' / brace are still read as inside the value.
        IReadOnlyDictionary<string, string> intros =
            EditorIntroParser.Parse("{\"AI\":\"a \\\"quoted\\\" word\"}");

        Assert.Equal("a \"quoted\" word", intros["AI"]);
    }

    [Theory]
    [InlineData("[\"a\",\"b\"]")]
    [InlineData("42")]
    [InlineData("\"just a string\"")]
    [InlineData("true")]
    public void Non_object_root_yields_empty_map(string raw)
    {
        // No top-level '{' (array/number/string/bool) -> nothing to extract.
        Assert.Empty(EditorIntroParser.Parse(raw));
    }

    [Fact]
    public void Skips_non_string_property_values_keeping_string_siblings()
    {
        IReadOnlyDictionary<string, string> intros = EditorIntroParser.Parse(
            "{\"AI\":\"keep\",\"Security\":123,\"Cloud\":null," +
            "\"Web\":{\"nested\":\"obj\"},\"Data\":[\"arr\"],\"Hardware\":\"alsoKeep\"}");

        Assert.Equal(2, intros.Count);
        Assert.Equal("keep", intros["AI"]);
        Assert.Equal("alsoKeep", intros["Hardware"]);
        Assert.False(intros.ContainsKey("Security"));
        Assert.False(intros.ContainsKey("Cloud"));
        Assert.False(intros.ContainsKey("Web"));
        Assert.False(intros.ContainsKey("Data"));
    }

    [Fact]
    public void Duplicate_key_last_value_wins()
    {
        IReadOnlyDictionary<string, string> intros =
            EditorIntroParser.Parse("{\"AI\":\"first\",\"AI\":\"second\"}");

        Assert.Equal("second", intros["AI"]);
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        // Model cased the key lower; callers look it up by the canonical category.
        IReadOnlyDictionary<string, string> intros =
            EditorIntroParser.Parse("{\"security\":\"Sec intro.\"}");

        Assert.Equal("Sec intro.", intros["Security"]);
        Assert.True(intros.ContainsKey("SECURITY"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_or_null_input_yields_empty_map(string? raw)
    {
        Assert.Empty(EditorIntroParser.Parse(raw));
    }

    [Fact]
    public void Garbage_without_braces_yields_empty_map()
    {
        Assert.Empty(EditorIntroParser.Parse("not json at all"));
    }

    [Fact]
    public void Malformed_json_inside_braces_yields_empty_map()
    {
        // A '{' is found, but the extracted span isn't valid JSON -> JsonException is
        // swallowed and the (empty) map is returned.
        Assert.Empty(EditorIntroParser.Parse("{\"AI\": }"));
    }

    [Fact]
    public void Blank_key_and_blank_value_entries_are_dropped()
    {
        IReadOnlyDictionary<string, string> intros = EditorIntroParser.Parse(
            "{\"\":\"orphan\",\"AI\":\"   \",\"Security\":\"keep\"}");

        // Blank key ("") and whitespace-only value ("   ") are both dropped; only the real
        // entry survives.
        Assert.Single(intros);
        Assert.Equal("keep", intros["Security"]);
    }

    [Fact]
    public void Key_and_value_are_trimmed()
    {
        IReadOnlyDictionary<string, string> intros =
            EditorIntroParser.Parse("{\"  AI  \":\"  AI intro.  \"}");

        Assert.Equal("AI intro.", intros["AI"]);
    }
}
