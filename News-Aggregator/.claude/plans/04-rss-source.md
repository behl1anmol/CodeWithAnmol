# Plan 04 — Implement RssNewsSource

## Context

`NewsAggregator.Infrastructure/Sources/RssNewsSource.cs` was a scaffold: DI-wired (named
`HttpClient` `"rss"` with **no** base address — feeds are absolute URLs; ctor
`(IHttpClientFactory, IOptions<SourceOptions>, ILogger<RssNewsSource>)`; `_options =
options.Value.Rss`; registered as a second `INewsSource` singleton in
`InfrastructureServiceCollectionExtensions.AddSources`) but `FetchAsync` threw
`NotImplementedException`. `System.ServiceModel.Syndication` (10.0.8) was already
referenced. This plan implements the generic, config-driven RSS/Atom adapter + tests,
reusing the patterns proven in `HackerNewsSource`. **Scope: the source file + one Core
options POCO + appsettings + one new test file.** No new abstractions, no architecture
change, no cross-feed dedup (that stays in `NewsAggregationService`).

Reused contracts (unchanged): `INewsSource`, `NewsItem` (required `Id/Title/Url(absolute)/
Source`, optional `Content/PublishedAt`), `NewsAggregationService` (fail-fast + owns
dedup), `FakeHttpMessageHandler` (reused as-is, not modified).

## Confirmed decisions (asked + answered)

1. **Configuration** → *Extend `RssOptions`* with `MaxItemsPerFeed` (20), `TimeoutSeconds`
   (30), `MaxConcurrency` (4). Bound from the existing `Sources:Rss` section; mirrored in
   `appsettings.json` for discoverability. BCL-only POCO → `DependencyRuleTests` stays
   green. Rationale: the task lists Max-Items/Timeout as configurable knobs; these are the
   only additions and avoid hardcoded magic values.
2. **Verification** → *Attempt SDK install* (no `dotnet` in container). Installed .NET SDK
   10.0.300 to `/tmp/dotnet` via `dotnet-install.sh`, then built + ran the suite.

## Files

- **Modified** `Core/Configuration/SourceOptions.cs` — three int props on `RssOptions`.
- **Modified** `Web/appsettings.json` — surface the three knobs under `Sources:Rss`.
- **Modified** `Infrastructure/Sources/RssNewsSource.cs` — real `FetchAsync` + helpers.
- **New** `Tests/Sources/RssNewsSourceTests.cs` — 14 tests (see below).

## Concurrency strategy

Feeds are independent → fan out one `Task<IReadOnlyList<NewsItem>>` per configured feed,
each acquiring a `SemaphoreSlim(Math.Max(1, MaxConcurrency))` permit before its HTTP GET.
`await Task.WhenAll` over the index-ordered task array preserves feed order for free →
deterministic merged output (feed order, then document/item order) with no concurrent
collection. Same primitive as `HackerNewsSource`, applied at feed granularity. Never
sequential.

## Failure / error-isolation strategy

Each feed runs in its own `try/catch` with its own per-feed deadline:
`CancellationTokenSource.CreateLinkedTokenSource(outerToken)` + `CancelAfter(TimeoutSeconds)`.
HTTP errors, malformed XML (`XmlException`), and per-feed timeouts are `LogWarning`-ed and
turned into an **empty** result for that feed only — a failing feed never faults the batch
or blocks healthy feeds. Invalid entries (blank title / no absolute link) are
`LogDebug`-skipped. The only exception that propagates is genuine caller cancellation
(`catch (OperationCanceledException) when (outerToken.IsCancellationRequested)` → rethrow),
preserving Core's fail-fast contract. Nothing swallowed silently.

## Feed parsing strategy

`GET feed` (`ResponseHeadersRead`, linked token) → `EnsureSuccessStatusCode()` →
`ReadAsStreamAsync(linked)` → `SyndicationFeed.Load(XmlReader.Create(stream))`
(auto-detects RSS 2.0 + Atom 1.0). `Items.Take(MaxItemsPerFeed)` mapped via `MapToNewsItem`.

## Domain mapping (NewsItem unchanged)

- **Title** = `item.Title?.Text`; blank → skip (`LogDebug`).
- **Url** = first `item.Links` with `Uri.IsAbsoluteUri`; none → skip. Rationale: `Url` is
  required + absolute; fabricating one would corrupt Core's URL-based dedup, so dropping
  the entry is the safest fallback.
- **Id** = `item.Id` (`<guid>`/Atom `id`) if non-blank, else the chosen absolute link.
  Rationale: feed-provided id is the stable identity; link is the safe fallback.
- **Source** = `"RSS"`. **Content** = `Summary?.Text` else `Content` text, raw (HTML kept,
  like HackerNews), null when blank. **PublishedAt** = `PublishDate` if `!= default` else
  `LastUpdatedTime` if `!= default` else null (Syndication uses MinValue for "absent").

Invalid feed URLs in config are parsed/validated up front and skipped (no request made).

## Tests (14, new file; existing untouched, `FakeHttpMessageHandler` reused)

1 valid-feed mapping, 2 guid-missing→link-id fallback, 3 skip no-title, 4 skip
non-absolute link, 5 empty feed, 6 malformed feed isolated, 7 multiple feeds merged in
order, 8 respects MaxItemsPerFeed, 9 disabled = no items + zero HTTP, 10 timed-out feed
isolated (responder throws `TaskCanceledException`, caller token uncancelled) + others
produce, 11 caller cancellation throws `OperationCanceledException`, 12 partial failure
(500) skips feed keeps others, 13 invalid config URL skipped, 14 Atom feed parsed.
All offline (canned XML), deterministic.

## Verification

`global.json` pins SDK `10.0.300`. Installed it to `/tmp/dotnet`; built + tested with that
on PATH:

```bash
export PATH=/tmp/dotnet:$PATH
dotnet build src/NewsAggregator.Infrastructure/NewsAggregator.Infrastructure.csproj   # 0 warn / 0 err
dotnet test  src/NewsAggregator.Tests/NewsAggregator.Tests.csproj                     # 102 passed, 0 failed
```

Result: Infrastructure build clean (0/0); **102 passed, 0 failed** (88 prior + 14 new);
zero live network calls.
