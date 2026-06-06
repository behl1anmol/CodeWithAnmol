using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application;
using NewsAggregator.Core.Application.Enrichment;
using NewsAggregator.Core.Application.Services;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Agents;
using NewsAggregator.Infrastructure.Caching;
using NewsAggregator.Infrastructure.Workflows;
using NewsAggregator.Tests.Fakes;
using Xunit;

namespace NewsAggregator.Tests.Workflows;

/// <summary>
/// P6 Definition-of-Done smoke test: the single end-to-end "refresh digest" path exercising
/// collect → enrich → compose → cache through the <b>real</b> coordinator and <b>real</b>
/// Microsoft Agent Framework workflows, offline and deterministic. Only the model is faked
/// (a per-title <see cref="IChatClient"/>), so a non-empty, categorized, score-ordered digest
/// proves the whole pipeline is wired correctly — the last gap in <c>docs/07 §7.5</c>.
/// </summary>
public sealed class DigestPipelineSmokeTests
{
    // Matches DigestApplicationService's private cache key (the documented "latest digest" slot).
    private const string CacheKey = "digest:latest";

    private static readonly DateTimeOffset FixedNow = new(2026, 6, 6, 9, 0, 0, TimeSpan.Zero);

    // Four articles spanning three taxonomy categories with distinct scores, so the digest has
    // a real multi-section, score-ordered shape (not a single trivial section). Section order by
    // top score: Security (0.9) → AI (0.7) → Cloud (0.3); Security items: A (0.9) then C (0.5).
    private static readonly IReadOnlyDictionary<string, (string Category, double Score)> ByTitle =
        new Dictionary<string, (string, double)>(StringComparer.Ordinal)
        {
            ["A"] = ("Security", 0.9),
            ["B"] = ("AI", 0.7),
            ["C"] = ("Security", 0.5),
            ["D"] = ("Cloud", 0.3),
        };

    private static IReadOnlyList<NewsItem> Articles()
        => [.. ByTitle.Keys.Select(title => new NewsItem
        {
            Id = title,
            Title = title,
            Url = new Uri($"https://example.com/{title}"),
            Source = "HackerNews",
            Content = $"Body of article {title}.",
        })];

    // Editor intros keyed by category (EditorIntroParser maps these back onto sections).
    private const string EditorJson =
        "{\"Security\":\"Sec intro.\",\"AI\":\"AI intro.\",\"Cloud\":\"Cloud intro.\"}";

    private static DigestApplicationService BuildPipeline(InMemoryDigestCache cache)
    {
        // Real aggregation over a deterministic source.
        var aggregation = new NewsAggregationService([new FakeNewsSource("HackerNews", Articles())]);

        // The Categorizer/Ranker reply depends on the article title (parsed from the workflow's
        // prompt); the Summarizer/Editor return fixed canned text. This makes "categorized,
        // score-ordered" a genuine assertion rather than every item collapsing into one section.
        var factory = new FakeAgentFactory(
            summary: "A neutral two sentence summary. It is factual.",
            categorizerJson: "unused", // replaced per-title by the client below
            rankerJson: "unused",      // replaced per-title by the client below
            editor: EditorJson,
            clientFactory: (role, reply) => role switch
            {
                AgentRole.Categorizer => new TitleAwareChatClient(title =>
                    $"{{\"category\":\"{ByTitle[title].Category}\",\"tags\":[\"tag-{title}\"]}}"),
                AgentRole.Ranker => new TitleAwareChatClient(title =>
                    $"{{\"score\":{ByTitle[title].Score.ToString(CultureInfo.InvariantCulture)},"
                    + "\"reason\":\"deterministic\"}"),
                _ => new FakeChatClient(reply), // Summarizer + Editor use their canned reply
            });

        var enrichment = new ConcurrentEnrichmentWorkflow(
            factory, Options.Create(new EnrichmentOptions { MaxDegreeOfParallelism = 4 }));
        var editorial = new SequentialEditorialWorkflow(factory, new FixedTimeProvider(FixedNow));

        return new DigestApplicationService(aggregation, enrichment, editorial, cache);
    }

    [Fact]
    public async Task Produces_a_categorized_score_ordered_digest_end_to_end()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new InMemoryDigestCache(memory);
        DigestApplicationService sut = BuildPipeline(cache);
        var progress = new RecordingProgress<AgentProgress>();

        Digest digest = await sut.RefreshDigestAsync(progress);

        // Non-empty + categorized: every section is a real taxonomy category and all four
        // articles survive, one per input.
        Assert.NotEmpty(digest.Sections);
        Assert.All(digest.Sections, s => Assert.Contains(s.Category, Taxonomy.Categories));
        Assert.Equal(4, digest.Sections.Sum(s => s.Items.Count));

        // Score-ordered: sections ranked by their top item's score (desc), and items within a
        // section ranked by score (desc) — the DigestComposer contract, end-to-end.
        Assert.Equal(["Security", "AI", "Cloud"], digest.Sections.Select(s => s.Category));
        double[] topScores = [.. digest.Sections.Select(s => s.Items[0].Relevance.Value)];
        Assert.Equal([.. topScores.OrderDescending()], topScores);
        DigestSection security = digest.Sections.Single(s => s.Category == "Security");
        Assert.Equal(["A", "C"], security.Items.Select(i => i.Item.Title));

        // Editor intros were mapped back onto the sections by category.
        Assert.Equal("Sec intro.", security.Intro);

        // Timestamp comes from the injected clock (deterministic).
        Assert.Equal(FixedNow, digest.GeneratedAt);
    }

    [Fact]
    public async Task Writes_the_result_to_the_cache()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new InMemoryDigestCache(memory);
        DigestApplicationService sut = BuildPipeline(cache);

        Digest digest = await sut.RefreshDigestAsync();

        // The composed digest round-trips from the cache under the latest-digest key, proving the
        // coordinator wrote it (the single-write property is covered by
        // DigestApplicationServiceOrchestrationTests with a counting fake).
        Digest? cached = await cache.GetAsync(CacheKey);
        Assert.Same(digest, cached);
    }

    [Fact]
    public async Task Reports_progress_stages_in_pipeline_order()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new InMemoryDigestCache(memory);
        DigestApplicationService sut = BuildPipeline(cache);
        var progress = new RecordingProgress<AgentProgress>();

        await sut.RefreshDigestAsync(progress);

        List<string> stages = [.. progress.Reports.Select(p => p.Stage)];
        Assert.Contains("collecting", stages);
        Assert.Contains("enriching", stages);
        Assert.Contains("composing", stages);
        Assert.Contains("done", stages);
        // First-occurrence order of each coarse stage matches the pipeline.
        Assert.True(stages.IndexOf("collecting") < stages.IndexOf("enriching"));
        Assert.True(stages.IndexOf("enriching") < stages.IndexOf("composing"));
        Assert.True(stages.IndexOf("composing") < stages.IndexOf("done"));
    }

    /// <summary>
    /// Deterministic <see cref="IChatClient"/> whose reply is a function of the article title
    /// parsed from the workflow prompt (which emits a <c>Title: &lt;t&gt;</c> line). Lets the
    /// Categorizer/Ranker return different category/score JSON per article while staying offline.
    /// </summary>
    private sealed class TitleAwareChatClient : IChatClient
    {
        private readonly Func<string, string> _replyForTitle;

        public TitleAwareChatClient(Func<string, string> replyForTitle)
            => _replyForTitle = replyForTitle;

        private static string TitleOf(IEnumerable<ChatMessage> messages)
        {
            string text = string.Concat(messages.Select(m => m.Text));
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Title:", StringComparison.Ordinal))
                {
                    return trimmed["Title:".Length..].Trim();
                }
            }

            return string.Empty;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, _replyForTitle(TitleOf(messages)))));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, _replyForTitle(TitleOf(messages)));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
