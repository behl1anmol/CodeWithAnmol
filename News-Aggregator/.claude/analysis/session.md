# Session Log — NewsAggregator.Core implementation + tests

**Project:** `/mnt/stuff/CommonProjects/CodeWithAnmol/News-Aggregator`
**Branch:** `feat/core-business-logic` (off `main`)
**PR:** [#3](https://github.com/behl1anmol/CodeWithAnmol/pull/3) — base `main`
**Commits:** `3f81e17` (Core logic), `2200e6d` (tests), `146b51b` (docs), `d00ddd0` (dedup fix)
**Stack:** .NET 10 (`net10.0`), C# 14 (`LangVersion=latest`), xUnit + NSubstitute, pragmatic Clean Architecture (Core / Infrastructure / Web / AppHost / Tests).

---

## Request 1 — Implement Core business logic

Scope: only `src/NewsAggregator.Core/`. No Infrastructure/Web/AppHost/Tests changes. No new NuGet
packages (Core enforced BCL-only by `DependencyRuleTests`). Replace `TODO(scaffold)` /
`NotImplementedException` where business logic belongs; implement aggregation + domain validation.

### Exploration (3 parallel Explore agents)
- Core path = `src/NewsAggregator.Core/`. Zero package references.
- **Only one** `NotImplementedException` in Core: `NewsAggregationService.CollectAsync`.
- `DigestApplicationService` already implemented; happy-path test green (NSubstitute).
- `RelevanceScore` already validates [0,1] + NaN; test green.
- `NewsItem` / `Digest` / `DigestSection` / `EnrichedItem` had no validation.
- Ports: `INewsSource`, `INewsAggregationService`, `IEnrichmentWorkflow`, `IEditorialWorkflow`,
  `IChatModelProvider`, `IDigestCache`. Domain records use value-equality; `PublishedAt` is
  `DateTimeOffset?`.

### Decisions (asked user; all "recommended" chosen)
1. **Dedupe rule** → canonical URL (lowercase scheme+authority, drop fragment, trim trailing slash, keep path+query).
2. **Source failure** → fail-fast / propagate (resilience+logging are Infrastructure concerns; Core has no logger).
3. **Validation style** → init-accessor with C# 14 `field` keyword, throwing (value-object style, no call-site changes, consistent with `RelevanceScore`).

### Implemented (5 files; `RelevanceScore.cs` untouched)
- `Application/Services/NewsAggregationService.cs` — `CollectAsync` = concurrent drain (`Task.WhenAll`, fail-fast) → order-by-Id → dedupe-by-canonical-URL → sort publish-date desc (nulls last, Id tie-break) → `AsReadOnly()`. Plus ctor null-guard.
- `Application/Services/DigestApplicationService.cs` — ctor null-guards only (no signature change → Web DI untouched; no clock added).
- `Domain/NewsItem.cs`, `Domain/Digest.cs`, `Domain/EnrichedItem.cs` — construction-time validation.

### Verification
SDK quirk: `src/global.json` pins SDK `10.0.300`, machine only has `10.0.100` → in-repo build fails.
Workaround: run `dotnet` from a neutral cwd (`/tmp`) so global.json isn't picked up → uses installed
GA SDK (supports net10.0 + C# 14). Result: Core build 0 warnings / 0 errors; 9/9 tests green.

---

## Request 2 — Add tests

Scope: only `src/NewsAggregator.Tests/`. xUnit; prefer fakes over mocks; no new mocking framework;
test behaviour not implementation. Tasks: tests for NewsAggregationService, DigestApplicationService,
all domain validation, edge cases, duplicate-removal, ordering, failure paths.

### Implemented (8 new files, additive; existing tests untouched)
- Fakes: `FakeNewsSource`, `ApplicationFakes` (4 port fakes w/ shared call-log + recorded inputs), `RecordingProgress<T>`.
- Tests: `NewsAggregationServiceTests`, `DigestApplicationServiceOrchestrationTests`, `NewsItemTests`, `DigestTests`, `EnrichedItemTests`.

### Verification
First run: 72/74; 2 wiring asserts used `Assert.Same` but fakes `.ToList()`-copy inputs (content equal,
identity differs) → switched to `Assert.Equal`. Final: **74 passed, 0 failed** (9 pre-existing + 65 new).

---

## Request 3 — Commit / push / PR
Two commits already on branch; pushed `feat/core-business-logic` to origin; opened PR #3 against `main`.

## Request 4 — Persist session
This `.claude/` folder: `plans/01-core-business-logic.md`, `plans/02-core-tests.md`,
`analysis/session.md` (this file), `analysis/implementation-summary.md`. Committed `146b51b`.

## Request 5 — PR review comment (P2): include title hash in de-dup
Comment on `NewsAggregationService.cs` (PR #3): code deduped by `CanonicalKey(item.Url)` only, but
docs §1 + the class XML doc say "canonical URL / title hash" → same article from two sources under
different URLs but same headline survives twice.

- **Analysis:** partially valid — real code-vs-spec mismatch, but URL-only was a deliberate earlier
  choice (title-OR merges distinct articles sharing a headline). Latent, not a live bug (no real
  `INewsSource` adapters built yet). Asked user → chose **add title-hash**.
- **Fix (`d00ddd0`):** drop item if canonical URL **OR** normalized title (lowercase,
  whitespace-collapsed via `TitleKey`) already seen; first occurrence (lowest `Id`) wins. Tests:
  factory title now defaults to `Id`; added `Removes_duplicates_by_normalized_title` +
  `Keeps_items_with_distinct_titles_and_urls`. **76 tests pass.**
- Reviewer reply attempt blocked by sandbox (outbound write out of scope); draft handed to user.
- **Accepted tradeoff:** two genuinely distinct articles sharing a headline now collapse to one.

---

## Request 6 — Implement HackerNewsSource (Infrastructure)

Scope: only `src/NewsAggregator.Infrastructure/Sources/HackerNewsSource.cs` (replace the
`NotImplementedException` scaffold) + new test files. No other source, no Core contract, no
DI/config change. Plan: `.claude/plans/03-hackernews-source.md`.

### Exploration (3 parallel Explore agents)
- Scaffold already DI-wired: named client `"hackernews"` → `https://hacker-news.firebaseio.com/v0/`,
  ctor `(IHttpClientFactory, IOptions<SourceOptions>, ILogger<HackerNewsSource>)`, singleton `INewsSource`.
- `HackerNewsOptions`: `Enabled` (true), `MaxItems` (30), `Story` (`topstories`). Reused as-is.
- `NewsAggregationService` is fail-fast (`Task.WhenAll`, exceptions propagate); transient resilience
  added globally in Web `ServiceDefaultsExtensions.AddStandardResilienceHandler()` → source adds no retry.
- No HTTP test double existed → created `FakeHttpMessageHandler`.
- `ImplicitUsings=enable` in `src/Directory.Build.props` → `System.Net.Http`/`Linq`/`Threading` already global.

### Decisions (asked user; all "recommended" chosen)
1. **Url for url-less posts** (Ask/Show/text/job) → fall back to HN permalink
   `https://news.ycombinator.com/item?id={id}`; external `url` used when absolute. (NewsItem.Url is required+absolute.)
2. **Top-list endpoint failure** (post global retries) → log error + **rethrow** (don't swallow; Core fail-fast). Per-item failures caught + skipped.
3. **Concurrency** → hardcoded `const int MaxConcurrency = 8` + `SemaphoreSlim` (keeps scope to the single file; no Core/appsettings touch).

### Implemented
- `FetchAsync` async iterator: honor `Enabled` → `GET {Story}.json` (ids) → `Take(MaxItems)` →
  fan-out `GET item/{id}.json` bounded by `SemaphoreSlim(8)` → `Task.WhenAll` (order-preserving → deterministic ranking order) → yield non-null.
- Helpers `FetchStoryIdsAsync` / `FetchItemAsync` / `MapToNewsItem` / `ResolveUrl` + private DTO `HackerNewsItem`.
- **Concurrency choice**: `SemaphoreSlim`+`Task.WhenAll` over `Parallel.ForEachAsync` — need bounded concurrency *and* ordered results (determinism) *and* a per-item `NewsItem?` to filter skips; WhenAll over indexed tasks gives ordering for free.
- **Error handling**: list-fetch error → `LogError`+rethrow (cancellation rethrown silently); per-item error (HTTP/JSON/mapping) → `LogWarning`+return null = skip, source survives; invalid payloads (null item / `deleted` / `dead` / blank title) → `LogDebug`+skip. Nothing swallowed silently.
- **Mapping**: `Id`=id `.ToString(Invariant)`; `Title`=`title`; `Url`=external-or-permalink; `Source`=`"HackerNews"`; `Content`=`text` (null if blank); `PublishedAt`=`FromUnixTimeSeconds(time)`. JSON binds via web defaults (camelCase, case-insensitive).

### Tests (new; existing untouched)
- `Fakes/FakeHttpMessageHandler.cs` — routes by `RequestUri.AbsolutePath`, records paths, responder `Func`, 200-JSON / status helpers.
- `Sources/HackerNewsSourceTests.cs` — 12 cases: maps top stories, permalink fallback, empty list, null list, skips deleted/dead, skips null payload, skips no-title, respects MaxItems (asserted via recorded paths), disabled = no items + zero HTTP, partial failure skips one, cancellation throws, ranking-order determinism. No live API.

### Verification
Build/test from `/tmp` (SDK pin). Infrastructure build 0 warnings / 0 errors. **88 passed, 0 failed** (76 prior + 12 new).

---

## Request 7 — Implement RssNewsSource (Infrastructure)

Scope: `src/NewsAggregator.Infrastructure/Sources/RssNewsSource.cs` (replace the
`NotImplementedException` scaffold) + minimal Core config + appsettings + one new test file.
No other source, no new abstractions, no architecture change. Cross-feed dedup stays in
`NewsAggregationService`. Branch `claude/rss-news-source-1IT8K`. Plan:
`.claude/plans/04-rss-source.md`.

### Exploration
- Scaffold already DI-wired: named client `"rss"` with **no** base address (feeds are
  absolute URLs), ctor `(IHttpClientFactory, IOptions<SourceOptions>, ILogger)`, singleton
  `INewsSource`. `System.ServiceModel.Syndication` (10.0.8) already referenced.
- `RssOptions` had only `Enabled` + `Feeds`. `FakeHttpMessageHandler` already exists →
  reused as-is. appsettings already has a `Sources:Rss` section with two real feeds.

### Decisions (asked user; both recommended chosen)
1. **Config** → extend `RssOptions` with `MaxItemsPerFeed` (20), `TimeoutSeconds` (30),
   `MaxConcurrency` (4); mirrored in appsettings. BCL-only → `DependencyRuleTests` green.
2. **Verification** → install SDK (no `dotnet` in this container — different machine than
   prior sessions). Installed 10.0.300 to `/tmp/dotnet` via `dotnet-install.sh`.

### Implemented
- `FetchAsync` async iterator: `Enabled` gate → validate feed URLs (skip non-absolute) →
  per-feed fan-out bounded by `SemaphoreSlim(MaxConcurrency)` → each feed gets a linked
  timeout CTS (`CreateLinkedTokenSource` + `CancelAfter(TimeoutSeconds)`) → `GetAsync`
  (`ResponseHeadersRead`) → `EnsureSuccessStatusCode` → `SyndicationFeed.Load(XmlReader)`
  (RSS 2.0 + Atom) → `Items.Take(MaxItemsPerFeed)` → map → `Task.WhenAll` (order-preserving
  → deterministic) → yield. Helpers: `ResolveFeedUrls`, `FetchFeedAsync`, `ParseFeed`,
  `MapToNewsItem`, `ResolveLink`, `ResolveContent`, `ResolvePublishedAt`.
- **Concurrency**: `SemaphoreSlim` + `Task.WhenAll` at feed granularity (bounded + ordered).
- **Error isolation**: per-feed `try/catch`; HTTP error / `XmlException` / per-feed timeout
  → `LogWarning` + empty list (that feed only). Invalid entries (blank title / no absolute
  link) → `LogDebug` + skip. Caller cancellation (`when outerToken.IsCancellationRequested`)
  → rethrow (fail-fast). Nothing swallowed.
- **Mapping**: `Id`=`<guid>`/Atom id else absolute link; `Title`=title; `Url`=first absolute
  link (skip entry if none — safest fallback vs fabricating a URL that would corrupt Core's
  URL dedup); `Source`="RSS"; `Content`=`Summary` else `Content` text (raw, null if blank);
  `PublishedAt`=`PublishDate` else `LastUpdatedTime` else null.

### Tests (new `Sources/RssNewsSourceTests.cs`, 14 cases; existing untouched)
valid mapping, guid→link id fallback, skip no-title, skip non-absolute link, empty feed,
malformed feed isolated, multiple feeds merged in order, MaxItemsPerFeed, disabled=no
items+zero HTTP, timed-out feed isolated (responder throws `TaskCanceledException`),
caller cancellation throws, partial failure (500) isolated, invalid config URL skipped,
Atom parsed. All offline via `FakeHttpMessageHandler`, deterministic.

### Verification
Installed SDK 10.0.300 → `dotnet build` Infrastructure 0/0; `dotnet test` **102 passed,
0 failed** (88 prior + 14 new). Zero live network calls.

---

## Notes / gotchas for future sessions
- **SDK pin**: build/test from `/tmp` (or any dir without a parent `global.json`) until SDK 10.0.300 is installed. Do NOT build AppHost that way (needs `Aspire.AppHost.Sdk` msbuild-sdk from global.json).
- **Core is BCL-only** — adding any package breaks `DependencyRuleTests` by design.
- Cannot change service ctor signatures without editing the Web composition root (out of allowed scope).
- `ArgumentException.ThrowIfNullOrWhiteSpace(null)` throws `ArgumentNullException` (subclass) → use `Assert.ThrowsAny<ArgumentException>` in mixed null/blank theories.
