namespace NewsAggregator.Core.Domain;

/// <summary>
/// The fixed roster of agent roles in the MVP. Used to select per-agent models
/// (configuration) and to tag progress events for the UI.
/// </summary>
public enum AgentRole
{
    Summarizer,
    Categorizer,
    Ranker,
    Editor,
}
