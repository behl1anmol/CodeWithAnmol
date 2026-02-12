using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Workflows;
using NewsAggregator.Tests.Fakes;
using Xunit;

namespace NewsAggregator.Tests.Workflows;

/// <summary>
/// P2: <see cref="ConcurrentEnrichmentWorkflow"/> runs Summarizer/Categorizer/Ranker per
/// article via the real Microsoft Agent Framework concurrent workflow, merges via the P1
/// assembler, streams progress, bounds cross-article parallelism, and is deterministic +
/// offline (backed by <see cref="FakeAgentFactory"/>/<see cref="FakeChatClient"/>).
/// </summary>
public sealed class ConcurrentEnrichmentWorkflowTests
{
    private const string Summary = "A neutral two sentence summary. It is factual.";
    private const string CategorizerJson = "{\"category\":\"Security\",\"tags\":[\"cve\",\"patch\"]}";
    private const string RankerJson = "{\"score\":0.6,\"reason\":\"notable\"}";

    private static ConcurrentEnrichmentWorkflow Workflow(
        FakeAgentFactory factory,
        int maxDegreeOfParallelism = 4)
        => new(factory, Options.Create(new EnrichmentOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
        }));

    private static FakeAgentFactory DefaultFactory()
        => new(Summary, CategorizerJson, RankerJson);

    private static IReadOnlyList<NewsItem> Items(int count)
        => [.. Enumerable.Range(0, count).Select(i => new NewsItem
        {
            Id = $"id-{i}",
            Title = $"Article {i}",
            Url = new Uri($"https://example.com/{i}"),
            Source = "HackerNews",
            Content = $"Body of article {i}.",
        })];

    [Fact]
    public async Task Produces_one_enriched_item_per_input_in_order()
    {
        IReadOnlyList<NewsItem> items = Items(5);

        IReadOnlyList<EnrichedItem> result = await Workflow(DefaultFactory()).EnrichAsync(items);

        Assert.Equal(5, result.Count);
        Assert.Equal(
            items.Select(i => i.Id),
            result.Select(r => r.Item.Id));
    }

    [Fact]
    public async Task Merges_agent_replies_via_the_assembler()
    {
        IReadOnlyList<EnrichedItem> result = await Workflow(DefaultFactory()).EnrichAsync(Items(1));

        EnrichedItem item = Assert.Single(result);
        Assert.Equal(Summary, item.Summary);
        Assert.Equal("Security", item.Category);
        Assert.Equal(["cve", "patch"], item.Tags);
        Assert.Equal(0.6, item.Relevance.Value);
        Assert.Equal("notable", item.Relevance.Reason);
    }

    [Fact]
    public async Task Empty_input_yields_empty_result()
    {
        IReadOnlyList<EnrichedItem> result = await Workflow(DefaultFactory()).EnrichAsync([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Junk_agent_output_still_yields_a_valid_item()
    {
        // Blank summary, non-JSON category, garbage score: the assembler must keep the
        // workflow total (no throw) and fall back to the domain's guarantees.
        var factory = new FakeAgentFactory("   ", "not json at all", "}{");

        IReadOnlyList<EnrichedItem> result = await Workflow(factory).EnrichAsync(Items(1));

        EnrichedItem item = Assert.Single(result);
        Assert.Equal("Other", item.Category);
        Assert.Equal("Body of article 0.", item.Summary); // content snippet fallback
        Assert.Empty(item.Tags);
        Assert.Equal(RelevanceScore.Zero, item.Relevance);
    }

    [Fact]
    public async Task Reports_progress_with_roles_and_a_completion_count()
    {
        var progress = new RecordingProgress<AgentProgress>();

        await Workflow(DefaultFactory()).EnrichAsync(Items(3), progress);

        Assert.NotEmpty(progress.Reports);
        Assert.All(progress.Reports, p => Assert.Equal("enriching", p.Stage));
        // Per-agent updates carry the live role...
        Assert.Contains(progress.Reports, p => p.Role == AgentRole.Summarizer);
        Assert.Contains(progress.Reports, p => p.Role == AgentRole.Categorizer);
        Assert.Contains(progress.Reports, p => p.Role == AgentRole.Ranker);
        // ...and the processed count advances to the total once every item completes.
        Assert.Equal(3, progress.Reports.Max(p => p.ProcessedCount ?? 0));
    }

    [Fact]
    public async Task Respects_max_degree_of_parallelism()
    {
        const int maxDop = 2;
        var tracker = new ArticleConcurrencyTracker();
        var factory = new FakeAgentFactory(
            Summary,
            CategorizerJson,
            RankerJson,
            clientFactory: (_, reply) =>
                new TrackingChatClient(reply, tracker, TimeSpan.FromMilliseconds(60)));

        await Workflow(factory, maxDop).EnrichAsync(Items(6));

        // Observed concurrent articles never exceeds the configured bound...
        Assert.True(
            tracker.Peak <= maxDop,
            $"Observed {tracker.Peak} concurrent articles, expected <= {maxDop}.");
        // ...and the workflow really did run articles in parallel (not serialized).
        Assert.True(tracker.Peak >= 2, $"Expected real cross-article concurrency, observed {tracker.Peak}.");
    }

    [Fact]
    public async Task Cancellation_throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Workflow(DefaultFactory()).EnrichAsync(Items(3), progress: null, cts.Token));
    }

    /// <summary>
    /// Tracks the peak number of distinct articles whose agents are running at once. All
    /// three agents of one article share the same user prompt, so they key to one article.
    /// </summary>
    private sealed class ArticleConcurrencyTracker
    {
        private readonly Dictionary<string, int> _active = new(StringComparer.Ordinal);
        private readonly object _lock = new();

        public int Peak { get; private set; }

        public IDisposable Enter(string key)
        {
            lock (_lock)
            {
                _active[key] = _active.TryGetValue(key, out int n) ? n + 1 : 1;
                if (_active.Count > Peak)
                {
                    Peak = _active.Count;
                }
            }

            return new Scope(this, key);
        }

        private void Exit(string key)
        {
            lock (_lock)
            {
                if (!_active.TryGetValue(key, out int n))
                {
                    return;
                }

                if (n <= 1)
                {
                    _active.Remove(key);
                }
                else
                {
                    _active[key] = n - 1;
                }
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly ArticleConcurrencyTracker _tracker;
            private readonly string _key;
            private bool _disposed;

            public Scope(ArticleConcurrencyTracker tracker, string key)
            {
                _tracker = tracker;
                _key = key;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _tracker.Exit(_key);
            }
        }
    }

    /// <summary>
    /// Canned <see cref="IChatClient"/> that records per-article concurrency (keyed by the
    /// user prompt) and holds the "turn" open for <paramref name="delay"/> so overlap is
    /// observable. The concurrent workflow streams, so the streaming path is what runs.
    /// </summary>
    private sealed class TrackingChatClient : IChatClient
    {
        private readonly string _reply;
        private readonly ArticleConcurrencyTracker _tracker;
        private readonly TimeSpan _delay;

        public TrackingChatClient(string reply, ArticleConcurrencyTracker tracker, TimeSpan delay)
        {
            _reply = reply;
            _tracker = tracker;
            _delay = delay;
        }

        private static string Key(IEnumerable<ChatMessage> messages)
            => string.Concat(messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using (_tracker.Enter(Key(messages)))
            {
                await Task.Delay(_delay, cancellationToken);
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, _reply));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using (_tracker.Enter(Key(messages)))
            {
                await Task.Delay(_delay, cancellationToken);
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, _reply);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
