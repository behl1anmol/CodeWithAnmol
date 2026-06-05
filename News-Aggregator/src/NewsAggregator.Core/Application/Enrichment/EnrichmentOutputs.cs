namespace NewsAggregator.Core.Application.Enrichment;

/// <summary>
/// Raw parsed output of the Categorizer agent. A plain POCO with no validation — it
/// carries exactly what the model emitted; <see cref="EnrichedItemAssembler"/> is what
/// sanitises it into the domain. Also serves as the <c>System.Text.Json</c> target for
/// the categorizer's <c>{"category":…,"tags":[…]}</c> contract.
/// </summary>
public sealed record CategoryResult
{
    public string? Category { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>
/// Raw parsed output of the Relevance Ranker agent. <see cref="Score"/> is nullable so a
/// missing field is distinguishable from a genuine <c>0</c> (the assembler maps "missing"
/// to <see cref="Domain.RelevanceScore.Zero"/> deliberately). Deserialization target for
/// the ranker's <c>{"score":…,"reason":…}</c> contract.
/// </summary>
public sealed record RelevanceResult
{
    public double? Score { get; init; }

    public string? Reason { get; init; }
}
