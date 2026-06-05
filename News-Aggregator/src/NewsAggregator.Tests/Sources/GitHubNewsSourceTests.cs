using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Sources;
using NewsAggregator.Tests.Fakes;
using NSubstitute;
using Xunit;

namespace NewsAggregator.Tests.Sources;

/// <summary>
/// Behaviour of the GitHub releases adapter against canned HTTP responses (never the live
/// API): mapping, the name→tag fallback, per-release skip rules, empty/malformed payloads,
/// multiple repos, MaxReleasesPerRepo, the disabled switch, per-repo timeout + partial-failure
/// isolation, rate-limit isolation, invalid-config skip, and caller cancellation. Cross-repo
/// de-dup is intentionally NOT exercised here — it belongs to <c>NewsAggregationService</c>.
/// </summary>
public sealed class GitHubNewsSourceTests
{
    private const string BaseUrl = "https://api.github.com/";
    private const string RepoA = "owner/repo-a";
    private const string RepoB = "owner/repo-b";
    private const string RepoAPath = "/repos/owner/repo-a/releases";
    private const string RepoBPath = "/repos/owner/repo-b/releases";

    // ---- helpers ------------------------------------------------------------

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static FakeHttpMessageHandler Router(Dictionary<string, Func<HttpResponseMessage>> routes)
        => new(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            return routes.TryGetValue(path, out Func<HttpResponseMessage>? make)
                ? make()
                : FakeHttpMessageHandler.Status(HttpStatusCode.NotFound);
        });

    private static GitHubNewsSource BuildSut(
        FakeHttpMessageHandler handler,
        IEnumerable<string> repositories,
        GitHubOptions? options = null)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(GitHubNewsSource.HttpClientName).Returns(client);

        GitHubOptions github = options ?? new GitHubOptions();
        github.Repositories = [.. repositories];
        var sources = new SourceOptions { GitHub = github };
        return new GitHubNewsSource(factory, Options.Create(sources), NullLogger<GitHubNewsSource>.Instance);
    }

    private static async Task<List<NewsItem>> DrainAsync(GitHubNewsSource sut, CancellationToken ct = default)
    {
        var items = new List<NewsItem>();
        await foreach (NewsItem item in sut.FetchAsync(ct))
        {
            items.Add(item);
        }

        return items;
    }

    // Serialises an array of release payloads as the GitHub API returns them.
    private static HttpResponseMessage Releases(params object[] releases)
        => Json(JsonSerializer.Serialize(releases));

    // ---- 1. successful retrieval + mapping ----------------------------------

    [Fact]
    public async Task Maps_releases_to_news_items()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(new
            {
                id = 101L,
                name = "Version 1.0",
                tag_name = "v1.0",
                html_url = "https://github.com/owner/repo-a/releases/tag/v1.0",
                body = "release notes",
                draft = false,
                published_at = "2024-01-02T03:04:05Z",
            }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [RepoA])));

        Assert.Equal("101", item.Id);
        Assert.Equal("Version 1.0", item.Title);
        Assert.Equal(new Uri("https://github.com/owner/repo-a/releases/tag/v1.0"), item.Url);
        Assert.Equal("GitHub", item.Source);
        Assert.Equal("release notes", item.Content);
        Assert.Equal(
            new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
            item.PublishedAt);
    }

    // ---- 2. name blank -> tag_name fallback ---------------------------------

    [Fact]
    public async Task Blank_name_falls_back_to_tag_name()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(new
            {
                id = 1L,
                name = "",
                tag_name = "v8.0.0",
                html_url = "https://github.com/owner/repo-a/releases/tag/v8.0.0",
            }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [RepoA])));

        Assert.Equal("v8.0.0", item.Title);
    }

    // ---- 3. per-release skip rules ------------------------------------------

    [Fact]
    public async Task Skips_release_without_absolute_html_url()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(
                new { id = 1L, name = "Relative", tag_name = "v1", html_url = "/relative/path" },
                new { id = 2L, name = "Kept", tag_name = "v2", html_url = "https://github.com/owner/repo-a/releases/tag/v2" }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [RepoA])));

        Assert.Equal("Kept", item.Title);
    }

    [Fact]
    public async Task Skips_release_without_name_or_tag()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(
                new { id = 1L, name = "", tag_name = "", html_url = "https://github.com/owner/repo-a/releases/tag/v1" },
                new { id = 2L, name = "Kept", tag_name = "v2", html_url = "https://github.com/owner/repo-a/releases/tag/v2" }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [RepoA])));

        Assert.Equal("Kept", item.Title);
    }

    // ---- 14. skip drafts ----------------------------------------------------

    [Fact]
    public async Task Skips_draft_releases()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(
                new { id = 1L, name = "Draft", tag_name = "v1", html_url = "https://github.com/owner/repo-a/releases/tag/v1", draft = true },
                new { id = 2L, name = "Published", tag_name = "v2", html_url = "https://github.com/owner/repo-a/releases/tag/v2", draft = false }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [RepoA])));

        Assert.Equal("Published", item.Title);
    }

    // ---- published_at fallback to created_at --------------------------------

    [Fact]
    public async Task Falls_back_to_created_at_when_published_at_absent()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(new
            {
                id = 1L,
                name = "No publish date",
                tag_name = "v1",
                html_url = "https://github.com/owner/repo-a/releases/tag/v1",
                created_at = "2023-05-06T07:08:09Z",
            }),
        });

        NewsItem item = Assert.Single(await DrainAsync(BuildSut(handler, [RepoA])));

        Assert.Equal(new DateTimeOffset(2023, 5, 6, 7, 8, 9, TimeSpan.Zero), item.PublishedAt);
    }

    // ---- 4. empty results ---------------------------------------------------

    [Fact]
    public async Task Empty_release_list_yields_nothing()
    {
        var handler = Router(new() { [RepoAPath] = () => Json("[]") });

        Assert.Empty(await DrainAsync(BuildSut(handler, [RepoA])));
    }

    // ---- 5. invalid payloads ------------------------------------------------

    [Fact]
    public async Task Malformed_json_is_isolated_and_yields_nothing()
    {
        var handler = Router(new() { [RepoAPath] = () => Json("this is not json <<<") });

        // No throw escapes: the bad payload is caught, logged and dropped.
        Assert.Empty(await DrainAsync(BuildSut(handler, [RepoA])));
    }

    // ---- 6. multiple repositories -------------------------------------------

    [Fact]
    public async Task Merges_items_from_multiple_repos_in_repo_then_item_order()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(
                new { id = 1L, name = "A1", tag_name = "v1", html_url = "https://github.com/owner/repo-a/releases/tag/v1" },
                new { id = 2L, name = "A2", tag_name = "v2", html_url = "https://github.com/owner/repo-a/releases/tag/v2" }),
            [RepoBPath] = () => Releases(
                new { id = 3L, name = "B1", tag_name = "v1", html_url = "https://github.com/owner/repo-b/releases/tag/v1" }),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler, [RepoA, RepoB]));

        Assert.Equal(["A1", "A2", "B1"], result.Select(i => i.Title));
    }

    // ---- 7. MaxReleasesPerRepo ----------------------------------------------

    [Fact]
    public async Task Respects_max_releases_per_repo()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(
                new { id = 1L, name = "one", tag_name = "v1", html_url = "https://github.com/owner/repo-a/releases/tag/v1" },
                new { id = 2L, name = "two", tag_name = "v2", html_url = "https://github.com/owner/repo-a/releases/tag/v2" },
                new { id = 3L, name = "three", tag_name = "v3", html_url = "https://github.com/owner/repo-a/releases/tag/v3" }),
        });
        var sut = BuildSut(handler, [RepoA], new GitHubOptions { MaxReleasesPerRepo = 2 });

        List<NewsItem> result = await DrainAsync(sut);

        Assert.Equal(["one", "two"], result.Select(i => i.Title));
    }

    // ---- 8. disabled switch -------------------------------------------------

    [Fact]
    public async Task Disabled_source_yields_nothing_and_makes_no_http_calls()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(new { id = 1L, name = "x", tag_name = "v1", html_url = "https://github.com/owner/repo-a/releases/tag/v1" }),
        });
        var sut = BuildSut(handler, [RepoA], new GitHubOptions { Enabled = false });

        Assert.Empty(await DrainAsync(sut));
        Assert.Empty(handler.RequestedPaths);
    }

    // ---- 9. partial failure (HTTP error) ------------------------------------

    [Fact]
    public async Task Partial_failure_skips_failed_repo_keeps_others()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError),
            [RepoBPath] = () => Releases(new { id = 3L, name = "B1", tag_name = "v1", html_url = "https://github.com/owner/repo-b/releases/tag/v1" }),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler, [RepoA, RepoB]));

        Assert.Equal(["B1"], result.Select(i => i.Title));
    }

    // ---- 10. rate-limit isolation -------------------------------------------

    [Fact]
    public async Task Rate_limited_repo_is_isolated_and_others_still_produce()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
                response.Headers.Add("X-RateLimit-Remaining", "0");
                return response;
            },
            [RepoBPath] = () => Releases(new { id = 3L, name = "B1", tag_name = "v1", html_url = "https://github.com/owner/repo-b/releases/tag/v1" }),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler, [RepoA, RepoB]));

        Assert.Equal(["B1"], result.Select(i => i.Title));
    }

    // ---- 11. caller cancellation --------------------------------------------

    [Fact]
    public async Task Cancellation_throws_operation_canceled()
    {
        var handler = Router(new()
        {
            [RepoAPath] = () => Releases(new { id = 1L, name = "x", tag_name = "v1", html_url = "https://github.com/owner/repo-a/releases/tag/v1" }),
        });
        var sut = BuildSut(handler, [RepoA]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DrainAsync(sut, cts.Token));
    }

    // ---- 12. invalid config slug --------------------------------------------

    [Fact]
    public async Task Invalid_repository_slug_is_skipped()
    {
        var handler = Router(new()
        {
            [RepoBPath] = () => Releases(new { id = 3L, name = "B1", tag_name = "v1", html_url = "https://github.com/owner/repo-b/releases/tag/v1" }),
        });

        // "not-a-repo" has no owner/name split -> dropped before any request; valid repo runs.
        List<NewsItem> result = await DrainAsync(BuildSut(handler, ["not-a-repo", RepoB]));

        Assert.Equal(["B1"], result.Select(i => i.Title));
        Assert.DoesNotContain("/repos/not-a-repo/releases", handler.RequestedPaths);
    }

    // ---- 13. per-repo timeout isolation -------------------------------------

    [Fact]
    public async Task Timed_out_repo_is_isolated_and_others_still_produce()
    {
        // Simulate the timeout HttpClient would surface: repo A throws TaskCanceledException
        // while the caller's token stays uncancelled, so it must be isolated (not rethrown).
        var handler = Router(new()
        {
            [RepoAPath] = () => throw new TaskCanceledException("simulated repo timeout"),
            [RepoBPath] = () => Releases(new { id = 3L, name = "B1", tag_name = "v1", html_url = "https://github.com/owner/repo-b/releases/tag/v1" }),
        });

        List<NewsItem> result = await DrainAsync(BuildSut(handler, [RepoA, RepoB]));

        Assert.Equal(["B1"], result.Select(i => i.Title));
    }
}
