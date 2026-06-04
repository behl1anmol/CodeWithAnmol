using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application;

/// <summary>
/// A provider-neutral progress event emitted while workflows run, so the UI can
/// show live progress. Infrastructure maps framework workflow events
/// (e.g. AgentResponseUpdateEvent) onto this type before reporting via
/// <see cref="IProgress{T}"/>; the UI then streams it over SignalR.
/// </summary>
public sealed record AgentProgress
{
    public AgentRole? Role { get; init; }

    /// <summary>Coarse stage label, e.g. "collecting", "enriching", "composing".</summary>
    public required string Stage { get; init; }

    public string? Message { get; init; }

    public int? ProcessedCount { get; init; }

    public int? TotalCount { get; init; }
}
