namespace NewsAggregator.Tests.Fakes;

/// <summary>
/// Minimal deterministic <see cref="TimeProvider"/> that always reports a fixed instant, so
/// the editorial workflow's <c>Digest.GeneratedAt</c> can be asserted exactly without
/// pulling in an external testing package (BCL <see cref="TimeProvider"/> only).
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
