using NewsAggregator.Core.Application;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Workflows;
using NewsAggregator.Tests.Fakes;
using Xunit;

namespace NewsAggregator.Tests.Workflows;

/// <summary>
/// P3: <see cref="SequentialEditorialWorkflow"/> composes enriched items into the final
/// <see cref="Digest"/> — deterministic structure from <c>DigestComposer</c> plus Editor
/// intros — running the real Microsoft Agent Framework single-agent workflow offline,
/// backed by <see cref="FakeAgentFactory"/>/<see cref="FakeChatClient"/>.
/// </summary>
public sealed class SequentialEditorialWorkflowTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);

    private static EnrichedItem Item(string title, string category, double score)
        => new()
        {
            Item = new NewsItem
            {
                Id = title,
                Title = title,
                Url = new Uri($"https://example.com/{Uri.EscapeDataString(title)}"),
                Source = "HackerNews",
                Content = $"Body of {title}.",
            },
            Summary = $"Summary of {title}.",
            Category = category,
            Relevance = new RelevanceScore(score),
        };

    // Two categories, Security ranked above AI, so section order is Security then AI.
    private static IReadOnlyList<EnrichedItem> TwoSectionItems()
        => [Item("sec", "Security", 0.9), Item("ai", "AI", 0.4)];

    private static SequentialEditorialWorkflow Workflow(string editorJson, TimeProvider? clock = null)
        => new(
            new FakeAgentFactory(
                summary: "unused",
                categorizerJson: "unused",
                rankerJson: "unused",
                editor: editorJson),
            clock ?? new FixedTimeProvider(FixedNow));

    [Fact]
    public async Task Maps_intros_onto_the_matching_sections()
    {
        const string editorJson = "{\"Security\":\"Sec intro.\",\"AI\":\"AI intro.\"}";

        Digest digest = await Workflow(editorJson).ComposeAsync(TwoSectionItems());

        Assert.Equal(["Security", "AI"], digest.Sections.Select(s => s.Category));
        Assert.Equal("Sec intro.", digest.Sections[0].Intro);
        Assert.Equal("AI intro.", digest.Sections[1].Intro);
    }

    [Fact]
    public async Task Intros_match_case_insensitively()
    {
        // Model cased the key differently than the canonical category — still maps.
        const string editorJson = "{\"security\":\"Sec intro.\"}";

        Digest digest = await Workflow(editorJson).ComposeAsync(TwoSectionItems());

        Assert.Equal("Sec intro.", digest.Sections.Single(s => s.Category == "Security").Intro);
    }

    [Fact]
    public async Task Missing_intro_leaves_section_intro_null()
    {
        // Only Security gets an intro; the AI section must stay null (intro is optional).
        const string editorJson = "{\"Security\":\"Sec intro.\"}";

        Digest digest = await Workflow(editorJson).ComposeAsync(TwoSectionItems());

        Assert.Equal("Sec intro.", digest.Sections.Single(s => s.Category == "Security").Intro);
        Assert.Null(digest.Sections.Single(s => s.Category == "AI").Intro);
    }

    [Fact]
    public async Task Junk_editor_output_leaves_all_intros_null_without_throwing()
    {
        Digest digest = await Workflow("not json at all").ComposeAsync(TwoSectionItems());

        Assert.All(digest.Sections, s => Assert.Null(s.Intro));
    }

    [Fact]
    public async Task Stamps_generated_at_from_the_injected_clock()
    {
        Digest digest = await Workflow("{\"AI\":\"x\"}").ComposeAsync(TwoSectionItems());

        Assert.Equal(FixedNow, digest.GeneratedAt);
    }

    [Fact]
    public async Task Empty_input_yields_an_empty_but_timestamped_digest()
    {
        Digest digest = await Workflow("{}").ComposeAsync([]);

        Assert.Empty(digest.Sections);
        Assert.Equal(FixedNow, digest.GeneratedAt);
    }

    [Fact]
    public async Task Reports_composing_progress_for_the_editor()
    {
        var progress = new RecordingProgress<AgentProgress>();

        await Workflow("{\"AI\":\"x\"}").ComposeAsync(TwoSectionItems(), progress);

        Assert.NotEmpty(progress.Reports);
        Assert.All(progress.Reports, p => Assert.Equal("composing", p.Stage));
        Assert.Contains(progress.Reports, p => p.Role == AgentRole.Editor);
    }

    [Fact]
    public async Task Cancellation_throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Workflow("{\"AI\":\"x\"}").ComposeAsync(TwoSectionItems(), progress: null, cts.Token));
    }
}
