using NewsAggregator.Core.Application.Enrichment;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Agents;

/// <summary>
/// System instructions per agent role. Kept here (not in Core) since prompts are an
/// Infrastructure detail. The Summarizer/Categorizer/Ranker prompts emit exactly the
/// contract that <see cref="EnrichedItemAssembler"/> parses (P1); the Editor prompt is
/// refined in P3.
/// </summary>
internal static class AgentInstructions
{
    // Single source of truth: the categorizer must pick from the Core taxonomy.
    private static readonly string TaxonomyList = string.Join(", ", Taxonomy.Categories);

    public static string For(AgentRole role) => role switch
    {
        AgentRole.Summarizer =>
            "Summarize the article in 2-3 neutral, factual sentences. Reply with the "
            + "summary text only — no opinions, headings, lists, or markdown.",
        AgentRole.Categorizer =>
            "Classify the article. Choose exactly one category from this taxonomy: "
            + $"{TaxonomyList}. Also provide up to five short topic tags. Reply with strict "
            + "minified JSON only — no prose, no code fences — in exactly this shape: "
            + "{\"category\":\"<one taxonomy value>\",\"tags\":[\"tag1\",\"tag2\"]}.",
        AgentRole.Ranker =>
            "Rate the article's technical significance from 0.0 (trivial) to 1.0 "
            + "(landmark). Reply with strict minified JSON only — no prose, no code fences "
            + "— in exactly this shape: {\"score\":0.0,\"reason\":\"<one short line>\"}.",
        AgentRole.Editor =>
            "You are the editor of a technology news digest. You receive category sections, "
            + "each listing its article titles. Write one short, neutral 1-2 sentence intro "
            + "per section that frames why it matters — no opinions or hype. Reply with strict "
            + "minified JSON only — no prose, no code fences — an object mapping each exact "
            + "category name to its intro string, in exactly this shape: "
            + "{\"AI\":\"<intro>\",\"Security\":\"<intro>\"}.",
        _ => "You are a helpful assistant.",
    };
}
