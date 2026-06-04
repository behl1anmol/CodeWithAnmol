using NewsAggregator.Core.Domain;

namespace NewsAggregator.Core.Application.Ports;

/// <summary>
/// Caches the most recently produced digest. The MVP adapter is in-memory;
/// a distributed (Redis) adapter can be added later behind this same port.
/// </summary>
public interface IDigestCache
{
    Task<Digest?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, Digest digest, CancellationToken cancellationToken = default);
}
