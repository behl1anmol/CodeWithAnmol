# Implementation Summary

Branch `feat/core-business-logic` → PR #3 (base `main`). Two commits.

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
3. Dedupe by canonical URL key: `{scheme}://{authority}{path-without-trailing-slash}{query}`, lowercased scheme+authority, fragment dropped. Keep first.
4. Sort `PublishedAt` descending (`Nullable.Compare(b, a)` → nulls last), tie-break `Id` ordinal.
5. Return `deduped.AsReadOnly()` (immutable `ReadOnlyCollection`).

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

## Test result
`74 passed, 0 failed` (9 pre-existing + 65 new). `DependencyRuleTests` confirms Core gained no
framework reference.

## Quality attributes honoured
SOLID (small ports, DI, no God class), no static state, no service locator, no framework coupling in
Core, unit-test friendly (pure orchestration over ports; hand-written fakes, no mocking framework in
new tests), deterministic output.
