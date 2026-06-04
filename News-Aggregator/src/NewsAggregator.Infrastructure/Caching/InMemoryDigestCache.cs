using Microsoft.Extensions.Caching.Memory;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Caching;

/// <summary>
/// MVP digest cache backed by <see cref="IMemoryCache"/>.
/// TODO(post-MVP): a DistributedDigestCache over IDistributedCache (Redis from
/// Aspire) can be added behind <see cref="IDigestCache"/> with no Core change.
/// </summary>
public sealed class InMemoryDigestCache : IDigestCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;

    public InMemoryDigestCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<Digest?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_cache.TryGetValue(key, out Digest? digest) ? digest : null);

    public Task SetAsync(string key, Digest digest, CancellationToken cancellationToken = default)
    {
        _cache.Set(key, digest, DefaultTtl);
        return Task.CompletedTask;
    }
}
