namespace NewsAggregator.Core.Domain;

/// <summary>
/// A normalized "tech significance" score in the inclusive range [0, 1],
/// produced by the Relevance Ranker agent, with a short justification.
/// </summary>
public readonly record struct RelevanceScore
{
    public RelevanceScore(double value, string? reason = null)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "Relevance score must be within [0, 1].");
        }

        Value = value;
        Reason = reason;
    }

    public double Value { get; }

    public string? Reason { get; }

    public static RelevanceScore Zero => new(0);
}
