using System.Text.Json;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Enrichment;

/// <summary>
/// Pure, framework-free mapper from the three enrichment agents' raw text replies to a
/// valid <see cref="EnrichedItem"/> (docs §3.3). It is deliberately <b>total</b>: no LLM
/// output — malformed JSON, missing fields, out-of-range scores, blank summaries — can
/// make it throw. That guarantee is what keeps the concurrent workflow (P2) and the
/// end-to-end smoke test (P6) free of runtime parse/construction failures.
/// </summary>
public static class EnrichedItemAssembler
{
    // A blank summary falls back to a content snippet; cap it so the fallback stays a
    // short, summary-like string rather than the whole article body.
    private const int MaxSummarySnippet = 500;

    // Categorizer contract allows up to five tags (docs §1.4).
    private const int MaxTags = 5;

    // Web defaults => case-insensitive property binding, matching the existing source
    // adapters (HackerNewsSource) and tolerant of casing in model output.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Builds a valid <see cref="EnrichedItem"/> from the source item plus the Summarizer
    /// (plain text), Categorizer (JSON) and Ranker (JSON) replies. Any argument may be
    /// null/blank/garbage; the result always satisfies <see cref="EnrichedItem"/>'s
    /// invariants.
    /// </summary>
    public static EnrichedItem Assemble(
        NewsItem item,
        string? summaryText,
        string? categorizerJson,
        string? rankerJson)
    {
        ArgumentNullException.ThrowIfNull(item);

        CategoryResult category = TryDeserialize<CategoryResult>(categorizerJson) ?? new CategoryResult();
        RelevanceResult relevance = TryDeserialize<RelevanceResult>(rankerJson) ?? new RelevanceResult();

        return new EnrichedItem
        {
            Item = item,
            Summary = ResolveSummary(summaryText, item),
            Category = Taxonomy.Normalize(category.Category),
            Tags = CleanTags(category.Tags),
            Relevance = ResolveRelevance(relevance),
        };
    }

    private static string ResolveSummary(string? summaryText, NewsItem item)
    {
        if (Clean(summaryText) is { } summary)
        {
            return summary;
        }

        if (Clean(item.Content) is { } content)
        {
            return content.Length > MaxSummarySnippet
                ? content[..MaxSummarySnippet].TrimEnd()
                : content;
        }

        // NewsItem guarantees a non-blank Title, so this last resort is always valid.
        return item.Title;
    }

    private static IReadOnlyList<string> CleanTags(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var result = new List<string>(MaxTags);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? tag in tags)
        {
            if (Clean(tag) is not { } clean || !seen.Add(clean))
            {
                continue;
            }

            result.Add(clean);
            if (result.Count == MaxTags)
            {
                break;
            }
        }

        return result;
    }

    private static RelevanceScore ResolveRelevance(RelevanceResult relevance)
    {
        if (relevance.Score is not { } score || double.IsNaN(score) || score < 0 || score > 1)
        {
            return RelevanceScore.Zero;
        }

        return new RelevanceScore(score, Clean(relevance.Reason));
    }

    private static T? TryDeserialize<T>(string? raw)
        where T : class
    {
        if (!TryExtractJsonObject(raw, out string json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the first balanced <c>{…}</c> object from arbitrary text, tolerating
    /// markdown code fences or prose around the JSON. String contents (and their escapes)
    /// are skipped so braces inside string values don't break brace matching.
    /// </summary>
    private static bool TryExtractJsonObject(string? text, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        json = text[start..(i + 1)];
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
