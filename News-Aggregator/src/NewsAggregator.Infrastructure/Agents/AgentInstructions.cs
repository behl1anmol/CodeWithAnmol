using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Agents;

/// <summary>
/// Placeholder system instructions per agent role. Kept here (not in Core) since
/// prompts are an Infrastructure detail. TODO(scaffold): refine prompts and add
/// structured-output / response-format constraints for Categorizer and Ranker.
/// </summary>
internal static class AgentInstructions
{
    public static string For(AgentRole role) => role switch
    {
        AgentRole.Summarizer =>
            "Summarize the article in 2-3 neutral sentences. No opinions.",
        AgentRole.Categorizer =>
            "Assign exactly one category and up to five tags from the tech taxonomy.",
        AgentRole.Ranker =>
            "Rate the article's tech significance from 0 to 1 with a one-line reason.",
        AgentRole.Editor =>
            "Order the items and write a short intro for each category section.",
        _ => "You are a helpful assistant.",
    };
}
