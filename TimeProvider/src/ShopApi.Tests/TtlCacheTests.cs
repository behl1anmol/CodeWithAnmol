using Microsoft.Extensions.Time.Testing;
using ShopApi.Caching;
using Xunit;

namespace ShopApi.Tests;

public class TtlCacheTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entry_expires_after_its_ttl()
    {
        var time = new FakeTimeProvider(Now);
        var cache = new TtlCache<string, string>(time, TimeSpan.FromMinutes(5));
        cache.Set("user:42", "Anmol");

        Assert.True(cache.TryGet("user:42", out _));

        time.Advance(TimeSpan.FromMinutes(6));

        Assert.False(cache.TryGet("user:42", out _));
    }
}
