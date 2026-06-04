using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Ports;

/// <summary>
/// The single use-case the UI calls. Coordinates collection → concurrent
/// enrichment → sequential editorial and returns the finished
/// <see cref="Digest"/>, reporting progress along the way.
/// Keeping this behind an interface means the UI has no business logic.
/// </summary>
public interface IDigestApplicationService
{
    Task<Digest> RefreshDigestAsync(
        IProgress<AgentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
