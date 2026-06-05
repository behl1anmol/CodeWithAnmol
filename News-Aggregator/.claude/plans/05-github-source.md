# Plan 05 — Implement GitHubNewsSource (GitHub Releases)

## Context

After `HackerNewsSource` (plan 03) and `RssNewsSource` (plan 04), this adds a **third**
`INewsSource` that gathers GitHub-based developer news. Scope was confined to
`NewsAggregator.Infrastructure` plus the existing `SourceOptions` config POCO, `appsettings.json`,
and one new test file. No new abstractions, no architecture change, no Core domain-contract
changes (`NewsItem`, `INewsSource`, `NewsAggregationService` untouched). Cross-source dedup
stays in `NewsAggregationService`. Patterns reused verbatim from `RssNewsSource`.

Reused contracts (unchanged): `INewsSource`, `NewsItem` (required `Id/Title/Url(absolute)/
Source`, optional `Content/PublishedAt`), `NewsAggregationService` (fail-fast + owns dedup),
`FakeHttpMessageHandler` (reused as-is, not modified).

## Source-selection rationale (confirmed: GitHub Releases)

`GET /repos/{owner}/{repo}/releases` over a **config-driven** repository list. Chosen because
it is (1) **stable** — a documented, versioned REST endpoint, not scraped HTML; (2) **public,
no-auth for MVP** — works unauthenticated for public repos (60 req/hr/IP); (3) **meaningful
tech news** — a new release of e.g. `dotnet/runtime` or `microsoft/vscode` is genuine
developer news; (4) **deterministic** and maps 1:1 to `NewsItem`. Rejected: Events API (noisy,
low-signal, volatile payloads), Trending (no API → fragile HTML scraping), Search API
(10 req/min unauth, non-deterministic, == "repo search" the task excludes). This mirrors
`RssNewsSource`'s per-feed pattern exactly, swapping feed URLs for `owner/repo` slugs.

## Files

- **Modified** `Core/Configuration/SourceOptions.cs` — added `GitHubOptions GitHub` plus a new
  `GitHubOptions` POCO (`Enabled`, `Repositories`, `MaxReleasesPerRepo`, `TimeoutSeconds`,
  `MaxConcurrency`). BCL-only → `DependencyRuleTests` stays green. No duplicate config
  structure; bound from the existing `Sources:GitHub` section.
- **Modified** `Web/appsettings.json` — added the `Sources:GitHub` block with example
  repositories (`dotnet/runtime`, `microsoft/vscode`). Config examples only — no hardcoded
  repos in code.
- **Modified** `Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
  — registered named client `"github"` (BaseAddress `https://api.github.com/`; required
  `User-Agent`, `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`
  headers) and the third `INewsSource` singleton.
- **New** `Infrastructure/Sources/GitHubNewsSource.cs` — the adapter.
- **New** `Tests/Sources/GitHubNewsSourceTests.cs` — 16 offline tests.

## Concurrency strategy

Repositories are independent → fan out one `Task<IReadOnlyList<NewsItem>>` per configured
repo, each acquiring a `SemaphoreSlim(Math.Max(1, MaxConcurrency))` permit before its HTTP GET.
`await Task.WhenAll` over the index-ordered array preserves repo order → deterministic merged
output (repo order, then GitHub's newest-first release order). One HTTP call per repo (no
nested fan-out) → minimal API usage. Same primitive as `RssNewsSource`, at repo granularity.
Never sequential.

## Rate-limit strategy

Only configured repos are called (no search/trending fan-out), one request per repo, bounded
concurrency — comfortably within the 60 req/hr unauthenticated budget. A `403`/`429` response
carrying `X-RateLimit-Remaining: 0` (or a `Retry-After` header) is detected before
`EnsureSuccessStatusCode`, logged with a dedicated warning, and isolated to that repo (empty
result). **No retry loop** — retries would break the determinism requirement; skip-and-log is
the established pattern.

## Failure / error-isolation strategy

Each repo runs in its own `try/catch` with its own per-repo deadline:
`CancellationTokenSource.CreateLinkedTokenSource(outerToken)` + `CancelAfter(TimeoutSeconds)`.
HTTP errors, malformed JSON, rate limits, and per-repo timeouts are `LogWarning`-ed and turned
into an **empty** result for that repo only — a failing repo never faults the batch or blocks
healthy repos. Invalid releases (draft / blank name+tag / non-http(s) `html_url`) are
`LogDebug`-skipped. The only exception that propagates is genuine caller cancellation
(`catch (OperationCanceledException) when (outerToken.IsCancellationRequested)` → rethrow),
preserving Core's fail-fast contract. Nothing swallowed silently.

## Domain mapping (NewsItem unchanged)

- **Id** = release `id` (stable GitHub numeric id) → invariant string.
- **Title** = `name`; blank → fall back to `tag_name` (releases often have no name but always
  a tag like `v8.0.0`, itself meaningful); both blank → skip.
- **Url** = `html_url` parsed as an absolute **http/https** `Uri`; missing/relative/non-web →
  skip. Rationale: `Url` is required + absolute and used for dedup; fabricating one would
  corrupt dedup, so dropping is the safest fallback. (Note: on Linux `Uri.TryCreate("/x",
  Absolute)` yields a `file://` URI, so the scheme is checked explicitly.)
- **Source** = `"GitHub"`.
- **Content** = `body` (markdown changelog) raw, null when blank (consistent with HN/RSS).
- **PublishedAt** = `published_at` ?? `created_at`; null if both absent. Draft releases are
  skipped entirely (unpublished, auth-only — never news).

Invalid `"owner/repo"` slugs in config are parsed/validated up front and skipped (no request).

## Tests (16, new file; existing untouched, `FakeHttpMessageHandler` reused)

1 mapping, 2 blank-name→tag fallback, 3 skip non-absolute html_url, 4 skip blank name+tag,
5 skip draft, 6 published_at→created_at fallback, 7 empty list, 8 malformed JSON isolated,
9 multiple repos merged in order, 10 respects MaxReleasesPerRepo, 11 disabled = no items +
zero HTTP, 12 partial failure (500) skips repo keeps others, 13 rate-limit (403 +
X-RateLimit-Remaining:0) isolated, 14 caller cancellation throws, 15 invalid config slug
skipped (no request), 16 per-repo timeout isolated (responder throws `TaskCanceledException`,
caller token uncancelled). All offline (canned JSON), deterministic, zero live GitHub calls.

## Verification

`global.json` pins SDK `10.0.300`. No `dotnet` in the container → installed it to `/tmp/dotnet`
via `dotnet-install.sh`, then built + tested with that on PATH:

```bash
export PATH=/tmp/dotnet:$PATH
dotnet build src/NewsAggregator.Infrastructure/NewsAggregator.Infrastructure.csproj   # 0 warn / 0 err
dotnet build src/NewsAggregator.Web/NewsAggregator.Web.csproj                         # 0 warn / 0 err
dotnet test  src/NewsAggregator.Tests/NewsAggregator.Tests.csproj                     # 119 passed, 0 failed
```

Result: builds clean (0/0); **119 passed, 0 failed** (103 prior + 16 new); zero live network
calls.
