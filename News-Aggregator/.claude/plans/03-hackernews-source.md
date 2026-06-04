# Plan 03 — Implement HackerNewsSource

## Context

`NewsAggregator.Infrastructure/Sources/HackerNewsSource.cs` was a scaffold: fully DI-wired
(named `HttpClient` `"hackernews"` → `https://hacker-news.firebaseio.com/v0/`, ctor
`(IHttpClientFactory, IOptions<SourceOptions>, ILogger<HackerNewsSource>)`, registered as
`INewsSource` singleton) but `FetchAsync` threw `NotImplementedException`. This plan
implements the real adapter over the keyless Hacker News Firebase API + focused tests.
**Scope: the one source file + new test files only.** No Core contract, no other source,
no DI change.

Reused contracts (unchanged):
- `INewsSource` — `SourceName`, `IAsyncEnumerable<NewsItem> FetchAsync(CancellationToken)`.
- `NewsItem` record — required `Id/Title/Url(absolute)/Source`, optional `Content/PublishedAt`.
- `HackerNewsOptions` — `Enabled`, `MaxItems` (30), `Story` (`topstories`). Used as-is.
- `NewsAggregationService` is fail-fast (`Task.WhenAll`, exceptions propagate); transient
  resilience added globally in Web `ServiceDefaultsExtensions.AddStandardResilienceHandler()`,
  so the source adds no retry of its own.

## Confirmed decisions (asked + answered)

1. **Url for text/Ask/Show/job posts (no `item.url`)** → fall back to HN permalink
   `https://news.ycombinator.com/item?id={id}`; use external `url` when present + absolute.
2. **Top-list endpoint failure** (post-retry) → log error + **rethrow** (don't swallow;
   Core decides). Per-*item* failures caught + logged + skipped.
3. **Concurrency limit** → hardcoded `const int MaxConcurrency = 8`, `SemaphoreSlim` gate
   (keeps scope to the single file).

## Concurrency strategy

`GET {Story}.json` → `long[]` (HN ranking order) → `Take(MaxItems)` → one
`Task<NewsItem?>` per id, each acquiring a `SemaphoreSlim(8)` permit before its
`GET item/{id}.json` → `await Task.WhenAll` → yield non-null in original order.

**Why SemaphoreSlim + Task.WhenAll over `Parallel.ForEachAsync`**: need bounded concurrency
+ order-preserving results (determinism) + a per-item `NewsItem?` return to filter nulls.
`Task.WhenAll` over indexed tasks gives ordered results for free; `Parallel.ForEachAsync`
would need extra ordered/concurrent-collection bookkeeping. Avoids sequential downloads.

## Error handling

- **List fetch**: cancellation → rethrow silently; any other error → `LogError` + rethrow.
  Empty/`null` list → `LogWarning` + yield nothing.
- **Per item**: cancellation → rethrow (aborts source); any other error (HTTP non-success,
  JSON, mapping) → `LogWarning` + return `null` (skip, source continues).
- **Invalid payloads** skipped + `LogDebug`: `null` item, `deleted`/`dead`, blank `title`.

## Domain mapping

`Id`=requested id `.ToString(Invariant)`; `Title`=`title` (skip if blank); `Url`=external
`url` if absolute else permalink; `Source`=`"HackerNews"`; `Content`=`text` (null if blank);
`PublishedAt`=`DateTimeOffset.FromUnixTimeSeconds(time)` or null. No `type` filter beyond
skip rules. `GetFromJsonAsync` uses web defaults (camelCase, case-insensitive) → HN's
lowercase fields bind with no attributes.

## Files

- **Modified** `src/NewsAggregator.Infrastructure/Sources/HackerNewsSource.cs` — replace
  `FetchAsync` body, add private helpers + `HackerNewsItem` DTO. Keep ctor/fields/
  `HttpClientName`/`SourceName`. Added usings: `System.Globalization`,
  `System.Net.Http.Json`, `System.Runtime.CompilerServices` (`System.Net.Http`/`Linq`/
  `Threading` already covered by `ImplicitUsings=enable` in `src/Directory.Build.props`).
- **New** `src/NewsAggregator.Tests/Fakes/FakeHttpMessageHandler.cs` — `HttpMessageHandler`
  double routing by `RequestUri.AbsolutePath`, records paths, responder `Func`.
- **New** `src/NewsAggregator.Tests/Sources/HackerNewsSourceTests.cs` — xUnit + native
  asserts; NSubstitute for `IHttpClientFactory`; `NullLogger<HackerNewsSource>.Instance`.

Test cases: (1) maps top stories, (2) text-post permalink fallback, (3) empty list,
(4) null list, (5) skips deleted/dead, (6) skips null item payload, (7) skips no-title,
(8) respects MaxItems, (9) disabled = no items + no HTTP calls, (10) partial failure skips
one keeps others, (11) cancellation throws, (12) emits in ranking order (determinism).

## Verification

`global.json` pins SDK `10.0.300` (only `10.0.100` installed) → build/test from `/tmp`:

```bash
cd /tmp && dotnet build <repo>/src/NewsAggregator.Infrastructure/NewsAggregator.Infrastructure.csproj
cd /tmp && dotnet test  <repo>/src/NewsAggregator.Tests/NewsAggregator.Tests.csproj
```

Expect clean build, all prior tests green, 12 new tests passing, zero live HN calls.
