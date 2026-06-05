using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.Sources;

/// <summary>
/// Adapter over the public GitHub REST API that surfaces repository <em>releases</em> as
/// tech news. Repositories are config-driven ("owner/repo"), so adding one is a configuration
/// change, never a code change (Open/Closed). Wired with a named <see cref="HttpClient"/> via
/// <see cref="IHttpClientFactory"/>. Runs unauthenticated (no token needed for public repos);
/// cross-source de-duplication is owned by <c>NewsAggregationService</c>.
/// </summary>
public sealed class GitHubNewsSource : INewsSource
{
    public const string HttpClientName = "github";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubNewsSource> _logger;

    public GitHubNewsSource(
        IHttpClientFactory httpClientFactory,
        IOptions<SourceOptions> options,
        ILogger<GitHubNewsSource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.GitHub;
        _logger = logger;
    }

    public string SourceName => "GitHub";

    public async IAsyncEnumerable<NewsItem> FetchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("GitHub source is disabled; yielding no items.");
            yield break;
        }

        // Parse + validate "owner/repo" slugs up front: a malformed config entry is skipped
        // (logged), never fabricated into a request. No hardcoded repos — all from config.
        RepoSlug[] repos = ResolveRepositories();
        if (repos.Length == 0)
        {
            _logger.LogInformation("GitHub source has no valid repositories configured; yielding no items.");
            yield break;
        }

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

        // Bounded fan-out across independent repositories. Task.WhenAll preserves task order,
        // so the merged output is deterministic (repo order, then GitHub's newest-first
        // release order) despite the concurrent fetches. Each task is self-contained: a
        // failing repo returns an empty list rather than faulting the batch.
        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
        Task<IReadOnlyList<NewsItem>>[] tasks =
            [.. repos.Select(repo => FetchRepoReleasesAsync(client, repo, gate, cancellationToken))];

        IReadOnlyList<NewsItem>[] results = await Task.WhenAll(tasks);

        int emitted = 0;
        foreach (IReadOnlyList<NewsItem> repoItems in results)
        {
            foreach (NewsItem item in repoItems)
            {
                emitted++;
                yield return item;
            }
        }

        _logger.LogInformation(
            "GitHub: fetched {RepoCount} repositories, emitted {Emitted} items.", repos.Length, emitted);
    }

    private RepoSlug[] ResolveRepositories()
    {
        var slugs = new List<RepoSlug>(_options.Repositories.Count);
        foreach (string raw in _options.Repositories)
        {
            if (RepoSlug.TryParse(raw, out RepoSlug slug))
            {
                slugs.Add(slug);
            }
            else
            {
                // Bad config entry: skip it, keep the rest of the repos working.
                _logger.LogWarning("Skipping GitHub repository '{Repository}': expected 'owner/repo'.", raw);
            }
        }

        return [.. slugs];
    }

    private async Task<IReadOnlyList<NewsItem>> FetchRepoReleasesAsync(
        HttpClient client,
        RepoSlug repo,
        SemaphoreSlim gate,
        CancellationToken outerToken)
    {
        await gate.WaitAsync(outerToken);

        // Per-repo deadline covering fetch + read. Linked to the caller's token so a real
        // cancellation still wins; a timeout cancels only this repo's linked token.
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        if (_options.TimeoutSeconds > 0)
        {
            linked.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        }

        int take = Math.Max(0, _options.MaxReleasesPerRepo);
        // per_page bounds the server-side page; Take below is a defensive client-side cap.
        string endpoint = $"repos/{repo.Owner}/{repo.Name}/releases?per_page={take}";

        try
        {
            using HttpResponseMessage response =
                await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, linked.Token);

            // Rate limiting is a transient, repo-isolated condition — surface it with a
            // dedicated diagnostic before the generic EnsureSuccessStatusCode path.
            if (IsRateLimited(response))
            {
                _logger.LogWarning(
                    "Skipping GitHub repository {Owner}/{Repo}: rate limited (HTTP {Status}).",
                    repo.Owner, repo.Name, (int)response.StatusCode);
                return [];
            }

            response.EnsureSuccessStatusCode();

            GitHubRelease[]? releases =
                await response.Content.ReadFromJsonAsync<GitHubRelease[]>(linked.Token);
            if (releases is null || releases.Length == 0)
            {
                return [];
            }

            var items = new List<NewsItem>();
            foreach (GitHubRelease release in releases.Take(take))
            {
                NewsItem? mapped = MapToNewsItem(release, repo);
                if (mapped is not null)
                {
                    items.Add(mapped);
                }
            }

            _logger.LogDebug("GitHub repository {Owner}/{Repo}: mapped {Count} items.", repo.Owner, repo.Name, items.Count);
            return items;
        }
        catch (OperationCanceledException) when (outerToken.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — fail-fast for the whole source
        }
        catch (Exception ex)
        {
            // Repo-level failure (HTTP error, malformed JSON, or per-repo timeout where the
            // caller's token is NOT cancelled): isolate it. Log and drop just this repo so
            // healthy repos still produce results. Never swallowed silently.
            _logger.LogWarning(ex, "Skipping GitHub repository {Owner}/{Repo}: fetch or parse failed.", repo.Owner, repo.Name);
            return [];
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        // GitHub signals rate limiting with 403 (primary) or 429 (secondary) plus either an
        // exhausted X-RateLimit-Remaining or a Retry-After hint.
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
        {
            return false;
        }

        if (response.Headers.RetryAfter is not null)
        {
            return true;
        }

        return response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? values)
            && values.FirstOrDefault() == "0";
    }

    private NewsItem? MapToNewsItem(GitHubRelease release, RepoSlug repo)
    {
        if (release.Draft)
        {
            // Drafts are unpublished and only visible to authenticated maintainers; never news.
            _logger.LogDebug("GitHub {Owner}/{Repo}: skipping draft release {Id}.", repo.Owner, repo.Name, release.Id);
            return null;
        }

        // Releases frequently have an empty name but always carry a tag (e.g. "v8.0.0"),
        // which is itself meaningful news; fall back to it before giving up.
        string? title = !string.IsNullOrWhiteSpace(release.Name) ? release.Name : release.TagName;
        if (string.IsNullOrWhiteSpace(title))
        {
            _logger.LogDebug("GitHub {Owner}/{Repo}: skipping release {Id} with no name or tag.", repo.Owner, repo.Name, release.Id);
            return null;
        }

        if (string.IsNullOrWhiteSpace(release.HtmlUrl) ||
            !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            // NewsItem.Url is required and must be absolute. With no usable link there is no
            // safe fallback (fabricating a URL would corrupt de-dup), so the entry is dropped.
            _logger.LogDebug("GitHub {Owner}/{Repo}: skipping release '{Title}' with no absolute html_url.", repo.Owner, repo.Name, title);
            return null;
        }

        return new NewsItem
        {
            Id = release.Id.ToString(CultureInfo.InvariantCulture),
            Title = title,
            Url = url,
            Source = SourceName,
            // Raw markdown changelog kept as-is (consistent with HackerNews/RSS), null when blank.
            Content = string.IsNullOrWhiteSpace(release.Body) ? null : release.Body,
            PublishedAt = release.PublishedAt ?? release.CreatedAt,
        };
    }

    /// <summary>Validated "owner/repo" pair. Parsing rejects anything without exactly one
    /// non-blank owner and name, so no request is ever made for a malformed slug.</summary>
    private readonly record struct RepoSlug(string Owner, string Name)
    {
        public static bool TryParse(string? raw, out RepoSlug slug)
        {
            slug = default;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string[] parts = raw.Trim().Split('/');
            if (parts.Length != 2 ||
                string.IsNullOrWhiteSpace(parts[0]) ||
                string.IsNullOrWhiteSpace(parts[1]))
            {
                return false;
            }

            slug = new RepoSlug(parts[0].Trim(), parts[1].Trim());
            return true;
        }
    }

    /// <summary>
    /// Subset of the GitHub release payload we map. GitHub uses snake_case, so each field is
    /// bound explicitly via <see cref="JsonPropertyNameAttribute"/>. Extra fields are ignored.
    /// </summary>
    private sealed record GitHubRelease
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; init; }
    }
}
