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

### Request 8 — PR #5 review (P2): enforce per-feed timeout during body read
Reviewer (Codex): with `ResponseHeadersRead`, `ReadAsStreamAsync` + synchronous
`SyndicationFeed.Load` read the live body without observing `linked.Token`, so a server
stalling mid-body bypasses `TimeoutSeconds` and holds a concurrency slot. **Valid.** Fix:
buffer the body via `ReadAsByteArrayAsync(linked.Token)` (download now covered by the
deadline), then `ParseFeed(byte[])` over a `MemoryStream` (in-memory, no network). Added
`Slow_body_after_headers_hits_per_feed_timeout_and_is_isolated` using a `StallingHttpContent`
test double (headers immediate, body blocks until the read token cancels) + `TimeoutSeconds=1`.
`dotnet test` **103 passed, 0 failed**.

---

## Request 9 — Current-state analysis + MVP-completion prompt plan (no code build)

**Branch:** `claude/news-aggregator-analysis-MrbTS` (off `main`). **Date:** 2026-06-05.
Task: analyse how far the implementation is vs the MVP in `docs/`, decompose the remaining work
into independently-buildable numbered prompts, and persist them so future sessions can build by
prompt number. No application code changed this session — documentation only.

### Deliverables
- **New** `.claude/analysis/current-state-analysis.md` — verified DONE/NOT-DONE inventory (read from
  source, not assumed), the gap-to-DoD table (G1–G8) mapped to prompts, sequencing DAG rationale,
  deviation register, and the post-MVP exclusions.
- **New** `.claude/prompts/mvp-completion-prompts.md` — six self-contained prompts (P1–P6) with
  goal / prerequisites / file scope / steps / constraints / Definition-of-Done each, a dependency
  graph, global rules, and verification commands.

### Decisions (asked user; constraint C — no assumptions)
1. **Plan scope** → **MVP Definition-of-Done only** (docs §7.5). Post-MVP roadmap excluded, listed
   for reference only.
2. **`GitHubNewsSource`** (a 3rd source, not in MVP docs §1.2/§1.5 which name only HN+RSS) →
   **keep it, document the deviation.** The remaining pipeline is source-count-agnostic (consumes
   the deduped `NewsItem[]`, groups by LLM category, not by source), so no prompt special-cases it.
3. **Prompt independence** → **sequential DAG, green at every step**; each prompt self-contained
   with explicit prerequisites; full suite must stay green to avoid an enhancement/bug-fix loop
   (constraint F).

### Gap found (the only NOT-DONE work to reach MVP DoD)
G1 agent-output→`EnrichedItem` mapper (missing) · G2 placeholder agent prompts · G3
`ConcurrentEnrichmentWorkflow` = `NotImplementedException` · G4 `SequentialEditorialWorkflow` =
`NotImplementedException` · G5 UI: no live progress, scaffold `catch (NotImplementedException)`, no
category/tag filtering · G6 no model-provider health check · G7 AppHost never pulls the Ollama
model (single `dotnet run` can't produce a digest) · G8 no workflow smoke test. Everything else
(5 projects, Core, 3 source adapters, providers, DI, composition root, AppHost wiring) is **done**;
last recorded run **119 passed, 0 failed**.

### Prompt map
P1 enrichment contract + Core mapper → P2 concurrent workflow & P3 sequential workflow → P4 Blazor
(progress + real refresh + filtering) & P6 AppHost model bootstrap + e2e smoke test; P5 provider
health check is independent.

---

## Request 10 — PR #7 review fix + docs version reconciliation

**Branch:** `claude/news-aggregator-analysis-MrbTS`. **Date:** 2026-06-05. Documentation only.

Codex review comment on PR #7 (`discussion_r3360542081`) flagged that the new prompt doc cited
stale pinned versions. **Verified against source — the reviewer was correct.** Actual repo pins:

| Package | Docs originally said | Repo actually pins (authoritative) |
|---------|----------------------|------------------------------------|
| `Microsoft.Agents.AI*` | `1.8.0` | **`1.9.0`** (`Directory.Packages.props`) |
| `Aspire.Hosting.AppHost` | `13.4.0` | **`13.4.2`** (`Directory.Packages.props`) |
| `Aspire.AppHost.Sdk` | `13.4.0` | **`13.4.2`** (`global.json`) |
| `Microsoft.Extensions.ServiceDiscovery` | `13.4.0` (grouped w/ Aspire) | **`10.6.0`** (tracks .NET 10 line) |
| `OllamaSharp` | `v4+` | **`5.4.25`** |
| `OpenAI` | (unversioned) | **`2.10.0`** |
| `Microsoft.Extensions.AI*` | `10.6.0` | `10.6.0` ✅ (unchanged) |

### Fixes
- **Commit `195235a`** — corrected `.claude/prompts/mvp-completion-prompts.md` (global rule 5, P2/P3
  workflow notes, P6 AppHost note) and `.claude/analysis/current-state-analysis.md`; made
  `Directory.Packages.props` + `global.json` the **authoritative** source (prompts now say to
  re-read those before coding) so numbers can't go stale again. Replied to the thread + resolved it.
- **This commit** — reconciled the `docs/` MVP chapters too (user asked): `docs/README` pinned-version
  block, `docs/05 §5.0` version tables (+ an "authoritative source" note and the ServiceDiscovery
  .NET-10-line callout), `docs/05 §5.1` OllamaSharp row, and the `v4+` mentions in `docs/04`/`docs/07`.
  Historical "originally targeted 1.8.0/13.4.0" notes are left intentionally for traceability.

### Gotcha recorded
The `docs/` chapters are the *design intent*; `src/Directory.Packages.props` + `src/global.json` are
the *source of truth* for versions. When they disagree, trust the repo files and reconcile the docs.

---

## Request 11 — Implement ConcurrentEnrichmentWorkflow (P2)

**Branch:** merged via PR #9, commit `122e25a`. Scope: `Infrastructure/Workflows/ConcurrentEnrichmentWorkflow.cs` (replace `NotImplementedException`) + new test fakes/tests.

Per-article fan-out Summarizer∥Categorizer∥Ranker via `AgentWorkflowBuilder.BuildConcurrent([s,c,r])`; input `List<ChatMessage>`; `TurnToken(emitEvents:true)` drives the agents and surfaces `AgentResponseUpdateEvent`s; route updates to roles by `AIAgent.Id` (aggregator message order is not guaranteed → parse AFTER the run, not in the aggregator); merge via the pure P1 `EnrichedItemAssembler` (total — junk output still yields a valid item); `AgentProgress{Stage="enriching"}`; cross-article parallelism bounded by `SemaphoreSlim(MaxDegreeOfParallelism)` + `Task.WhenAll` (input order preserved); genuine caller cancellation rethrown via `ThrowIfCancellationRequested` (`WatchStreamAsync` ends silently on cancel).

### Implemented
- `Infrastructure/Workflows/ConcurrentEnrichmentWorkflow.cs`
- `Tests/Fakes/FakeAgentFactory.cs` — canned reply per role, optional `clientFactory` hook, caches one agent per role.
- `Tests/Workflows/ConcurrentEnrichmentWorkflowTests.cs` — 7 tests.

### Verification
**150 passed, 0 failed** (143 prior + 7 new).

---

## Request 12 — Fix per-refresh agent/client leak (P2 follow-up)

**Commit:** `37d71f9`. `AgentFrameworkAgentFactory` was building an `IChatClient`-backed agent per role per article → retained provider/HTTP resources on every digest refresh. Fix: cache one `AIAgent` per role for the factory lifetime via `ConcurrentDictionary<AgentRole, Lazy<AIAgent>>` (agents are stateless across runs and safe to reuse concurrently). `FakeAgentFactory` mirrors the same caching so tests exercise the reuse path.

---

## Request 13 — Implement SequentialEditorialWorkflow (P3)

**Branch:** `feat/p3-sequential-editorial-workflow`. **Date:** 2026-06-05. Scope per `.claude/prompts/mvp-completion-prompts.md` P3. Deterministic structure in pure Core; intros from the Editor agent.

### Decisions (asked user)
- **Constraint C — `Digest.GeneratedAt` clock:** inject `TimeProvider` (chosen) vs `DateTimeOffset.UtcNow`. Rationale: deterministic exact-timestamp tests via `FixedTimeProvider`; `TimeProvider` is BCL (no package); additive change.

### Implemented
- `Core/Application/Editorial/DigestComposer.cs` — pure: `OrderByDescending(Relevance.Value).ThenBy(Item.Title, Ordinal)` (stable) → `GroupBy(Category, Ordinal)` (first-appearance = section rank) → intro-less `DigestSection`s; empty→empty; total.
- `Core/Application/Editorial/EditorIntroParser.cs` — pure, total: first balanced `{…}` → category→intro map (OrdinalIgnoreCase), string values only, drops blanks; own brace-matcher; never throws on fences/prose/garbage.
- `Infrastructure/Workflows/SequentialEditorialWorkflow.cs` — DigestComposer → if 0 sections return empty `Digest` (skip LLM) → else Editor via `AgentWorkflowBuilder.BuildSequential([editor])` + `InProcessExecution.RunStreamingAsync` + `TurnToken(emitEvents:true)` + `WatchStreamAsync` (accumulate by `editor.Id`), `AgentProgress{Role=Editor,Stage="composing"}`, `ThrowIfCancellationRequested` → `EditorIntroParser.Parse` → map intros by category → `Digest{GeneratedAt=_clock.GetUtcNow(), Sections}`.
- `Infrastructure/Agents/AgentInstructions.cs` — Editor prompt → strict minified JSON object category→intro (no prose/fences).
- DI: `TryAddSingleton(TimeProvider.System)` in `InfrastructureServiceCollectionExtensions` (additive; no Web/Program.cs edit).
- `Tests/Application/DigestComposerTests.cs` — 6 tests.
- `Tests/Workflows/SequentialEditorialWorkflowTests.cs` — 8 tests.
- `Tests/Fakes/FixedTimeProvider.cs` — local BCL `TimeProvider` fake (no external testing package).

### Verification
Infra 0/0, Web 0/0, **164 passed, 0 failed** (150 prior + 14 new). `BuildSequential` single-agent path runtime-verified by passing workflow tests. `DependencyRuleTests` still green (both new Core files BCL-only).

---

## Request 14 — Persist P2/P3 session data + checkpoints; branch/commit/PR

Documentation request: capture the P2/P3 work into `.claude/` so future sessions resume cold, then branch + commit + push + open a PR. Added a new `.claude/checkpoints/` log (`README.md`, `p2-concurrent-enrichment.md`, `p3-sequential-editorial.md`), updated `.claude/analysis/current-state-analysis.md`, this `session.md`, `.claude/analysis/implementation-summary.md`, and bumped the test-count/status in `.claude/skills/news-aggregator-dev/SKILL.md`. (Note: the request initially said a `.copilot` folder by mistake; corrected to `.claude` — keep one knowledge base only.)

---

## Request 15 — Resolve PR #10 review comments (P3 follow-up)

**Branch:** `feat/p3-sequential-editorial-workflow` (committed directly onto the PR head, per
explicit user decision, so PR #10 updates in place). **Date:** 2026-06-05. Three review
threads resolved — two 🟡 substantive, one 🔵 nit — then `.claude` updated.

### Comments & fixes
1. **🟡 `EditorIntroParser.cs:66` — scanner untested.** Added
   `Tests/Application/EditorIntroParserTests.cs` (**19 tests**) pinning the balanced-brace
   scanner + per-entry filtering directly (was only reached via the workflow happy path + one
   garbage case). Covers the reviewer's full list: fenced/prose-wrapped object, **brace inside
   a string value**, escaped quote, non-object root (array/number/string/bool → empty),
   non-string values skipped (string siblings kept), **duplicate-key-last-wins**, case-
   insensitive lookup, null/blank/garbage/malformed → empty, blank key/value dropped, trimming.
2. **🟡 `DigestComposer.cs:34` — comparer mismatch.** User chose *align the comparer*:
   `GroupBy(Category, StringComparer.Ordinal)` → `OrdinalIgnoreCase`, so the composer, the
   `EditorIntroParser` map, and the workflow lookup all agree. No behavioural change today
   (categories arrive canonical from `Taxonomy.Normalize`); prevents a hypothetical `"AI"`/
   `"ai"` split from producing a section the intro map could never key. `group.Key` keeps the
   first item's (canonical) casing. Stays BCL-only → `DependencyRuleTests` green.
3. **🔵 `SequentialEditorialWorkflow.cs:109` — per-event alloc.** Hoisted one constant
   `AgentProgress` before the stream loop and gated `progress?.Report` on non-empty streamed
   text (skip empty/terminal deltas) — one allocation, progress tracks real content. Existing
   `Reports_composing_progress_for_the_editor` still green (canned `{"AI":"x"}` streams
   non-empty).

### Verification
dotnet **was not installed** in this container (skill's "already installed" note is stale for
this environment) → installed SDK 10.0.300 via `dotnet-install.sh` to `$HOME/.dotnet`. Infra
build 0/0, Web build 0/0, **183 passed, 0 failed** (164 prior + 19 new). Pushed to the PR
branch; resolved the three GitHub review threads with a reply each.

---

## Notes / gotchas for future sessions
- **SDK pin / install**: `src/global.json` pins `10.0.300` (`rollForward: latestFeature`). Fresh web containers may ship **no** `dotnet` at all — install it with `curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 10.0.300 --install-dir $HOME/.dotnet` then `export PATH=$HOME/.dotnet:$PATH`. (If only an older GA SDK is present, building from `/tmp` — a dir without a parent `global.json` — also works.) Do NOT build AppHost that way (needs `Aspire.AppHost.Sdk` msbuild-sdk from global.json).
- **Versions**: authoritative = `src/Directory.Packages.props` + `src/global.json` (Agent Framework `1.9.0`, M.E.AI `10.6.0`, Aspire hosting `13.4.2`, ServiceDiscovery `10.6.0`, `OllamaSharp` `5.4.25`, `OpenAI` `2.10.0`), **not** the `docs/` chapters' original targets.
- **Core is BCL-only** — adding any package breaks `DependencyRuleTests` by design.
- Cannot change service ctor signatures without editing the Web composition root (out of allowed scope).
- `ArgumentException.ThrowIfNullOrWhiteSpace(null)` throws `ArgumentNullException` (subclass) → use `Assert.ThrowsAny<ArgumentException>` in mixed null/blank theories.
- **Agent Framework workflows (1.9.0):** send a `TurnToken(emitEvents:true)` or the run hangs `Idle`; route streamed `AgentResponseUpdateEvent`s by `AIAgent.Id` (aggregator order is not guaranteed); `WatchStreamAsync` ends WITHOUT throwing on cancel, so call `ThrowIfCancellationRequested()` after the loop. `BuildSequential([singleAgent])` works and streams identically to `BuildConcurrent`.
- **Editorial determinism:** the sort/group lives in the pure Core `DigestComposer`; only section intros come from the Editor agent. `EditorIntroParser` is total like `EnrichedItemAssembler`. (It duplicates the brace-matcher rather than refactor the in-scope-frozen P1 assembler — candidate to hoist to one shared Core JSON helper.)
- **Clock:** the editorial workflow takes an injected `TimeProvider` (registered `TryAddSingleton(TimeProvider.System)`); tests use the local `FixedTimeProvider` fake (no external testing package).

---

## Session — P5: Model-provider health check & startup validation

**Branch:** `feat/p5-model-provider-health-check` (off `main`). **Date:** 2026-06-06.
Prompt P5 from `.claude/prompts/mvp-completion-prompts.md` (gap G6). Fully independent of P1–P4/P6.

### User decisions (AskUserQuestion)
1. **Registration:** in `Web/Program.cs`; Infrastructure takes only
   `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` (canonical "library implements
   `IHealthCheck`" pattern; smallest footprint).
2. **Scope:** stay minimal — check + registration + tests. **No** AppHost `.WithHttpHealthCheck`
   dashboard wiring (that drifts toward P6); no production exposure of `/health`.

### Changes
- **New** `Infrastructure/HealthChecks/ModelProviderHealthCheck.cs` (`IHealthCheck`). Branches on
  `ModelOptions.Provider`: **Ollama** → `GET {endpoint}/api/tags`, `IsSuccessStatusCode` ⇒ Healthy
  else Unhealthy; **OpenRouter** → `GET {base}` with **no** auth header, *any* response ⇒ Healthy
  (reachability, not a completion). `HttpRequestException` / non-caller `TaskCanceledException`
  (the bounded timeout) ⇒ Unhealthy; genuine caller cancel propagates. Key never on the request
  nor in any description.
- **`InfrastructureServiceCollectionExtensions`** — named client `model-provider-health` with a
  5 s `Timeout` (caps total probe time even under ServiceDefaults' resilience handler).
- **`Web/Program.cs`** — `AddHealthChecks().AddCheck<ModelProviderHealthCheck>("model-provider",
  tags: ["ready"])`. Accumulates onto the ServiceDefaults builder; surfaced by the existing
  `MapDefaultEndpoints()` `/health` (dev-only); tag `ready` keeps it off the `live`-only `/alive`.
- **Packages** — added `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` **10.0.8**
  (verified on nuget; same assembly the AspNetCore shared framework ships at 10.0.8 → unifies in
  Web, **no warning**) to `Directory.Packages.props` + Infrastructure csproj.
- **New** `Tests/HealthChecks/ModelProviderHealthCheckTests.cs` (9): Ollama 200/500/transport-throw/
  timeout; OpenRouter 200 + 401-still-reachable + unreachable; key-never-surfaced (reachable +
  unreachable). All via `FakeHttpMessageHandler` + NSubstitute `IHttpClientFactory`.

### Verification
SDK 10.0.300 (`$HOME/.dotnet`). Infra build **0/0**, Web build **0/0**, full suite
**204 passed, 0 failed** (195 prior + 9 new). `docs/` left untouched — docs/05 §5.3 + docs/06 §74
already describe the probe as a present-tense feature (now true; no operator-step change).

### Notes / gotchas
- **`IHealthCheck` is in the AspNetCore shared framework**, present in the Web SDK
  (`Microsoft.NET.Sdk.Web`) but **not** in a plain `Microsoft.NET.Sdk` library → Infrastructure
  needs the standalone `…HealthChecks.Abstractions` package to implement it.
- **OpenRouter probe = reachability only**: status code is intentionally ignored (401/404 still
  prove routability) so the check never needs the BYOK key and never requires a real completion.
- The probe client is **single-shot**: `RemoveAllResilienceHandlers()` opts it out of the global
  standard resilience handler (`ConfigureHttpClientDefaults`) so it never retries, and a 5 s
  `HttpClient.Timeout` caps it.

### PR #12 — caveman-review fixes (6 findings, none blocking)
1. **🟡 OpenRouter 5xx ⇒ Unhealthy** — was `Healthy` on *any* response; now `(int)StatusCode >= 500`
   ⇒ Unhealthy (provider degraded), 4xx still Healthy (routable). Symmetric-ish with Ollama.
2. **🟡 catch broadened** — the two specific catches (`HttpRequestException` + bounded
   `TaskCanceledException`) → one `catch (Exception ex) when (!cancellationToken.IsCancellationRequested)`
   so a resilience-pipeline fault / `UriFormatException` also yields Unhealthy; genuine caller
   cancel still propagates.
3. **🔵 probe client single-shot** — added `Microsoft.Extensions.Http.Resilience` to Infrastructure
   and `.RemoveAllResilienceHandlers()` on the named client (no retry backoff; the comment's "fast"
   is now true). API is `[Experimental(EXTEXP0001)]` → suppressed locally with `#pragma` wrapping
   the whole statement (the diagnostic attaches to the statement-start line, so the `disable` must
   precede `services.AddHttpClient`, not sit mid-chain).
4. **🔵 nits** — OpenRouter endpoint `TrimEnd('/')` for parity with Ollama; `"ready"`-tag comment
   reworded (the **absence of `"live"`**, not the `"ready"` tag, is what excludes it from `/alive`).
5. New tests: OpenRouter 503 ⇒ Unhealthy; unexpected-exception (stands in for a circuit-breaker
   fault) ⇒ Unhealthy. **206 passed, 0 failed** (was 204). Infra + Web build **0/0**.
