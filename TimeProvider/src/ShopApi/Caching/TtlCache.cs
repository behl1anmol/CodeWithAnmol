namespace ShopApi.Caching;

public sealed class TtlCache<TKey, TValue>(TimeProvider timeProvider, TimeSpan ttl)
    where TKey : notnull
{
    private readonly Dictionary<TKey, (TValue Value, DateTimeOffset StoredAt)> _entries = new();

    public void Set(TKey key, TValue value) =>
        _entries[key] = (value, timeProvider.GetUtcNow());

    public bool TryGet(TKey key, out TValue? value)
    {
        value = default;
        if (!_entries.TryGetValue(key, out var entry))
            return false;

        if (timeProvider.GetUtcNow() - entry.StoredAt >= ttl)
        {
            _entries.Remove(key);
            return false;
        }

        value = entry.Value;
        return true;
    }
}
