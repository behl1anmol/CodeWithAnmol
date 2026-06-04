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
