# Session Log — NewsAggregator.Core implementation + tests

**Project:** `/mnt/stuff/CommonProjects/CodeWithAnmol/News-Aggregator`
**Branch:** `feat/core-business-logic` (off `main`)
**PR:** [#3](https://github.com/behl1anmol/CodeWithAnmol/pull/3) — base `main`
**Commits:** `3f81e17` (Core logic), `2200e6d` (tests)
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
`analysis/session.md` (this file), `analysis/implementation-summary.md`.

---

## Notes / gotchas for future sessions
- **SDK pin**: build/test from `/tmp` (or any dir without a parent `global.json`) until SDK 10.0.300 is installed. Do NOT build AppHost that way (needs `Aspire.AppHost.Sdk` msbuild-sdk from global.json).
- **Core is BCL-only** — adding any package breaks `DependencyRuleTests` by design.
- Cannot change service ctor signatures without editing the Web composition root (out of allowed scope).
- `ArgumentException.ThrowIfNullOrWhiteSpace(null)` throws `ArgumentNullException` (subclass) → use `Assert.ThrowsAny<ArgumentException>` in mixed null/blank theories.
