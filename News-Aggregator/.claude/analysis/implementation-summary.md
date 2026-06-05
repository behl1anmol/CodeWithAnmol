# Implementation Summary

Branch `feat/core-business-logic` → PR #3 (base `main`). Four commits: `3f81e17` Core, `2200e6d`
tests, `146b51b` session docs, `d00ddd0` dedup fix (PR review).

## Commit 1 — `3f81e17` Core business logic (5 files)

| File | Change | Rationale |
|------|--------|-----------|
| `src/NewsAggregator.Core/Application/Services/NewsAggregationService.cs` | Implemented `CollectAsync`; ctor `ArgumentNullException.ThrowIfNull` | Only `NotImplementedException` in Core; the merge/dedupe/sort logic |
| `src/NewsAggregator.Core/Application/Services/DigestApplicationService.cs` | 4 ctor null-guards | "Validate inputs" without changing the signature (Web DI untouched) |
| `src/NewsAggregator.Core/Domain/NewsItem.cs` | init-accessor validation | Id/Title/Source non-blank; Url absolute |
| `src/NewsAggregator.Core/Domain/Digest.cs` | init-accessor validation | `GeneratedAt`≠default; Sections non-null/no-null-entry; DigestSection.Category non-blank; Items no-null-entry |
| `src/NewsAggregator.Core/Domain/EnrichedItem.cs` | init-accessor validation | Item non-null; Summary/Category non-blank; Tags no null/blank entries |

`RelevanceScore.cs` deliberately unchanged (already validates [0,1] + NaN).

### CollectAsync algorithm
1. Fan out one `DrainAsync` task per `INewsSource`; `Task.WhenAll` (fail-fast — exceptions propagate).
2. Flatten, `OrderBy(Id, Ordinal)` so the duplicate survivor is deterministic under concurrency.
3. Dedupe by canonical URL **OR** normalized title (updated in `d00ddd0` — see below). Keep first.
4. Sort `PublishedAt` descending (`Nullable.Compare(b, a)` → nulls last), tie-break `Id` ordinal.
5. Return `deduped.AsReadOnly()` (immutable `ReadOnlyCollection`).

### Dedup keys
- **URL key** (`CanonicalKey`): `{scheme}://{authority}{path-without-trailing-slash}{query}`, lowercased scheme+authority, fragment dropped.
- **Title key** (`TitleKey`): `title.ToLowerInvariant()` split on whitespace + rejoined with single space (case-insensitive, whitespace-collapsed).
- Drop item if **either** key already seen. Tradeoff: distinct articles sharing a headline collapse to one (accepted per docs §1 + user decision).

### Validation pattern (C# 14 `field` keyword)
```csharp
public required string Title
{
    get => field;
    init { ArgumentException.ThrowIfNullOrWhiteSpace(value); field = value; }
}
```
Keeps `new(){...}` object-initializer + `with` working; enforces invariants at construction; throws
like the existing `RelevanceScore`. All BCL helpers → no package added → Core stays framework-free.

## Commit 2 — `2200e6d` Tests (8 files, additive)

**Fakes** (`src/NewsAggregator.Tests/Fakes/`): `FakeNewsSource`, `ApplicationFakes`
(`FakeNewsAggregationService` / `FakeEnrichmentWorkflow` / `FakeEditorialWorkflow` / `FakeDigestCache`,
each with optional shared `List<string>` call-log + recorded inputs), `RecordingProgress<T>`.

**Tests**:
- `Application/NewsAggregationServiceTests.cs` — merge, dedupe (canonical-URL variants + query-kept + deterministic lowest-Id survivor), ordering (desc / nulls-last / Id tie-break / stable across runs), edge (no sources / empty source / read-only), failure (throwing source propagates; cancellation).
- `Application/DigestApplicationServiceOrchestrationTests.cs` — order `collect→enrich→compose→cache`, data wiring, single cache write, progress stages, null-progress safe, 4 ctor null-guard facts.
- `Domain/NewsItemTests.cs`, `Domain/DigestTests.cs`, `Domain/EnrichedItemTests.cs` — all validation rules + defaults + `with`-revalidation.

## Commit 4 — `d00ddd0` Dedup fix (PR review P2)
Review flagged URL-only dedup vs docs' "canonical URL / title hash". Added `TitleKey` + dedup on URL
OR title. Tests: factory title defaults to `Id`; added `Removes_duplicates_by_normalized_title` +
`Keeps_items_with_distinct_titles_and_urls`. See `analysis/session.md` Request 5 for full rationale.

## Test result
`76 passed, 0 failed` (9 pre-existing + 67 new). `DependencyRuleTests` confirms Core gained no
framework reference.

## Quality attributes honoured
SOLID (small ports, DI, no God class), no static state, no service locator, no framework coupling in
Core, unit-test friendly (pure orchestration over ports; hand-written fakes, no mocking framework in
new tests), deterministic output.

---

# Infrastructure — HackerNewsSource (same branch, later commit)

Implemented `src/NewsAggregator.Infrastructure/Sources/HackerNewsSource.cs` (was a
`NotImplementedException` scaffold). Plan: `.claude/plans/03-hackernews-source.md`. Scope: that one
source file + 2 new test files. No Core contract, no `SourceOptions`, no DI/config change.

| File | Change |
|------|--------|
| `src/NewsAggregator.Infrastructure/Sources/HackerNewsSource.cs` | implemented `FetchAsync` + helpers + DTO |
| `src/NewsAggregator.Tests/Fakes/FakeHttpMessageHandler.cs` | new — routes by path, records requests |
| `src/NewsAggregator.Tests/Sources/HackerNewsSourceTests.cs` | new — 12 tests |

### Algorithm
`Enabled` gate → `GET {Story}.json` (ids, ranking order) → `Take(MaxItems)` → fan-out
`GET item/{id}.json` bounded by `SemaphoreSlim(8)` → `Task.WhenAll` (preserves order → deterministic) →
map → yield non-null.

### Decisions (asked user)
1. Url for url-less posts → HN permalink `…/item?id={id}` (external url when absolute).
2. Top-list failure → log + rethrow (Core fail-fast); per-item failure → skip.
3. Concurrency → hardcoded `const MaxConcurrency = 8` + `SemaphoreSlim` (single-file scope).

### Concurrency rationale
`SemaphoreSlim` + `Task.WhenAll` over `Parallel.ForEachAsync`: need bounded concurrency **and**
order-preserving results (determinism) **and** per-item `NewsItem?` to filter skips; WhenAll over
indexed tasks gives ordering for free. No sequential download.

### Error handling
List endpoint error → `LogError`+rethrow (cancellation rethrown silently); per-item error → `LogWarning`+skip (source survives); invalid payloads (null/`deleted`/`dead`/blank title) → `LogDebug`+skip. Nothing swallowed.

### Mapping (NewsItem unchanged)
`Id`=id `.ToString(Invariant)`; `Title`=`title`; `Url`=external-or-permalink; `Source`=`"HackerNews"`; `Content`=`text` (null if blank); `PublishedAt`=`FromUnixTimeSeconds(time)`. Binds via JSON web defaults (camelCase, case-insensitive).

### Test result
**88 passed, 0 failed** (76 prior + 12 new). No live HN API calls — all HTTP via `FakeHttpMessageHandler`.

---

# Infrastructure — RssNewsSource (same branch lineage, branch `claude/rss-news-source-1IT8K`)

Implemented `src/NewsAggregator.Infrastructure/Sources/RssNewsSource.cs` (was a
`NotImplementedException` scaffold). Plan: `.claude/plans/04-rss-source.md`. Generic,
config-driven RSS/Atom adapter. Cross-feed dedup stays in `NewsAggregationService`.

| File | Change |
|------|--------|
| `src/NewsAggregator.Core/Configuration/SourceOptions.cs` | `RssOptions` += `MaxItemsPerFeed` (20), `TimeoutSeconds` (30), `MaxConcurrency` (4) |
| `src/NewsAggregator.Web/appsettings.json` | surface the 3 new `Sources:Rss` knobs |
| `src/NewsAggregator.Infrastructure/Sources/RssNewsSource.cs` | implemented `FetchAsync` + helpers |
| `src/NewsAggregator.Tests/Sources/RssNewsSourceTests.cs` | new — 14 tests (`FakeHttpMessageHandler` reused, not modified) |

### Algorithm
`Enabled` gate → parse+validate feed URLs (skip non-absolute) → per-feed fan-out bounded by
`SemaphoreSlim(MaxConcurrency)` → each feed: linked-timeout CTS → `GetAsync` →
`EnsureSuccessStatusCode` → `SyndicationFeed.Load(XmlReader)` (RSS 2.0 + Atom) →
`Items.Take(MaxItemsPerFeed)` → map → `Task.WhenAll` (order-preserving → deterministic) →
yield. No cross-feed dedup.

### Decisions (asked user)
1. Config → **extend `RssOptions`** (MaxItemsPerFeed/TimeoutSeconds/MaxConcurrency); not hardcoded.
2. Verification → **install SDK 10.0.300** (`/tmp/dotnet`) and run the suite.

### Concurrency rationale
`SemaphoreSlim` + `Task.WhenAll` at *feed* granularity — bounded concurrency **and**
order-preserving deterministic merge. No sequential download.

### Error handling
Per-feed `try/catch` + own linked-timeout CTS. HTTP error / `XmlException` / per-feed
timeout → `LogWarning` + empty result for that feed only (isolated). Invalid entries (blank
title / no absolute link) → `LogDebug` + skip. Caller cancellation
(`when (outerToken.IsCancellationRequested)`) → rethrow (fail-fast). Nothing swallowed.

### Mapping (NewsItem unchanged)
`Id`=`<guid>`/Atom id else absolute link; `Title`=title (skip if blank); `Url`=first
absolute link (skip if none — safest fallback vs fabricating a URL); `Source`=`"RSS"`;
`Content`=`Summary` else `Content` text, raw, null if blank; `PublishedAt`=`PublishDate`
else `LastUpdatedTime` else null.

### Test result
**102 passed, 0 failed** (88 prior + 14 new). No live feed calls — all HTTP via `FakeHttpMessageHandler`.

---

# Infrastructure — GitHubNewsSource (third source)

Implemented `src/NewsAggregator.Infrastructure/Sources/GitHubNewsSource.cs`, a third `INewsSource`
adapter beyond the docs' HN+RSS pair. Retained by user decision; documented as the only deviation
from the spec. Config-driven; same bounded-fan-out pattern as the other sources (`SemaphoreSlim` +
`Task.WhenAll`, order-preserving, error-isolated, fail-fast on caller cancel). Offline tests via
`FakeHttpMessageHandler`.

### Test result
**119 passed, 0 failed** — pre-P1 baseline. The remaining pipeline is source-count-agnostic: it
consumes the deduped `NewsItem[]` and groups by LLM category, not by source.

---

# P1 — Enrichment output contract & Core mapper

Pure, framework-free contract turning the 3 enrichment agents' replies into a valid `EnrichedItem`.

| File | Change |
|------|--------|
| `Core/Application/Enrichment/Taxonomy.cs` | new — closed category set (AI, Security, Cloud, Devtools, Web, Data, Hardware, Other) + total `Normalize` |
| `Core/Application/Enrichment/EnrichmentOutputs.cs` | new — `CategoryResult` / `RelevanceResult` POCOs (System.Text.Json targets) |
| `Core/Application/Enrichment/EnrichedItemAssembler.cs` | new — pure, **total** mapper: raw summary/categorizer-JSON/ranker-JSON → valid `EnrichedItem`, never throws |
| `Infrastructure/Agents/AgentInstructions.cs` | Summarizer/Categorizer/Ranker prompts emit the exact contract (plain text / strict minified JSON; taxonomy from `Taxonomy.Categories`) |
| `Tests/Application/EnrichedItemAssemblerTests.cs` | new |

Assembler is total: tolerates markdown fences/prose (extracts first balanced `{…}`); blank summary →
content-snippet then title fallback; category blank/unknown → "Other"; tags drop blank/dedupe/cap 5;
score missing/NaN/out-of-range → `RelevanceScore.Zero`. BCL-only (System.Text.Json).

### Test result
**143 passed, 0 failed.**

---

# P2 — Concurrent enrichment workflow

| File | Change |
|------|--------|
| `Infrastructure/Workflows/ConcurrentEnrichmentWorkflow.cs` | implemented `EnrichAsync` (replaced NotImplementedException) |
| `Infrastructure/Agents/AgentFrameworkAgentFactory.cs` | cache one `AIAgent` per role (`ConcurrentDictionary<AgentRole, Lazy<AIAgent>>`) — leak fix (commit 37d71f9) |
| `Tests/Fakes/FakeAgentFactory.cs` | new — canned reply per role, optional `clientFactory` hook, one-agent-per-role caching |
| `Tests/Workflows/ConcurrentEnrichmentWorkflowTests.cs` | new — 7 tests |

Per-article fan-out Summarizer∥Categorizer∥Ranker via `AgentWorkflowBuilder.BuildConcurrent`;
`TurnToken(emitEvents:true)` + `WatchStreamAsync`, route updates by `AIAgent.Id`; merge via the
pure P1 `EnrichedItemAssembler`; `AgentProgress{Stage="enriching"}`; cross-article parallelism
bounded by `SemaphoreSlim(MaxDegreeOfParallelism)` + `Task.WhenAll` (input order preserved);
cancellation rethrown. Commits 122e25a + 37d71f9 (PR #9).

### Test result
**150 passed, 0 failed.**

---

# P3 — Sequential editorial workflow

| File | Change |
|------|--------|
| `Core/Application/Editorial/DigestComposer.cs` | new — pure: sort by `Relevance.Value` desc + stable `Item.Title` ordinal tie-break → `GroupBy(Category)` (first-appearance = section rank) → intro-less `DigestSection`s; total |
| `Core/Application/Editorial/EditorIntroParser.cs` | new — pure, total: first balanced `{…}` → category→intro map (OrdinalIgnoreCase); never throws |
| `Infrastructure/Workflows/SequentialEditorialWorkflow.cs` | implemented `ComposeAsync` (replaced NotImplementedException) |
| `Infrastructure/Agents/AgentInstructions.cs` | Editor prompt → strict minified JSON object category→intro |
| `Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | `TryAddSingleton(TimeProvider.System)` (additive) |
| `Tests/Application/DigestComposerTests.cs` | new — 6 tests |
| `Tests/Workflows/SequentialEditorialWorkflowTests.cs` | new — 8 tests |
| `Tests/Fakes/FixedTimeProvider.cs` | new — local BCL `TimeProvider` fake |

`ComposeAsync`: `DigestComposer` → if 0 sections return empty `Digest` (skip the LLM) → else Editor
via `AgentWorkflowBuilder.BuildSequential([editor])` + `RunStreamingAsync` + `TurnToken(emitEvents:true)`
+ `WatchStreamAsync` (accumulate by `editor.Id`), `AgentProgress{Role=Editor,Stage="composing"}`,
`ThrowIfCancellationRequested` → `EditorIntroParser.Parse` → map intros by category (`section with {Intro}`)
→ `Digest{GeneratedAt=_clock.GetUtcNow(), Sections}`. **Clock = injected `TimeProvider`** (user decision;
deterministic tests via `FixedTimeProvider`; BCL, no package). `DependencyRuleTests` still green (both
new Core files BCL-only). Tradeoff: `EditorIntroParser` duplicates the brace-matcher rather than refactor
the in-scope-frozen P1 assembler.

### Test result
**164 passed, 0 failed.** Infra + Web build 0 warn / 0 err.
