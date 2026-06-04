# Plan — Tests for NewsAggregator.Core

## Context

Core business logic was just implemented (branch `feat/core-business-logic`):
`NewsAggregationService.CollectAsync` (merge/dedupe-by-canonical-URL/sort), `DigestApplicationService`
ctor guards, and value-object validation on `NewsItem`/`Digest`/`DigestSection`/`EnrichedItem`.
Current tests (9) only cover `RelevanceScore`, the DigestApplicationService happy path (NSubstitute),
the dependency rule, and the fake chat client. The new Core behaviour is **untested**. Add behaviour
tests, working **only inside `src/NewsAggregator.Tests`**.

Constraints (from request): xUnit; no new mocking frameworks (NSubstitute already present but **prefer
fakes**); test behaviour not implementation. Additive only — do not modify existing test files.

Test project already references Core + Infrastructure, has xUnit + NSubstitute, and a `Fakes/` folder
with `FakeChatClient` (sealed, deterministic, XML-doc'd) — match that style.

## Files added (8) — all under `src/NewsAggregator.Tests/`, no existing file touched

**Fakes (hand-written, no mocking framework):**
- `Fakes/FakeNewsSource.cs` — `INewsSource` yielding a configured `NewsItem` list via async iterator; optional `Exception` to throw (failure path); honours `CancellationToken` per item.
- `Fakes/ApplicationFakes.cs` — `FakeNewsAggregationService`, `FakeEnrichmentWorkflow`, `FakeEditorialWorkflow`, `FakeDigestCache`. Each takes an optional shared `List<string>` call-log and records what it received (e.g. `ReceivedItems`, `SetKey`/`SetDigest`/`SetCount`).
- `Fakes/RecordingProgress.cs` — `RecordingProgress<T> : IProgress<T>` collecting reports synchronously (avoids `Progress<T>` sync-context flakiness).

**Tests:**
- `Application/NewsAggregationServiceTests.cs`
- `Application/DigestApplicationServiceOrchestrationTests.cs` (fake-based; existing NSubstitute file left as-is)
- `Domain/NewsItemTests.cs`
- `Domain/DigestTests.cs`
- `Domain/EnrichedItemTests.cs`

`RelevanceScore` already has full validation tests → not duplicated.

## Coverage (maps to the 7 tasks)

**NewsAggregationServiceTests** (task 1, 4, 5, 6, 7):
- Merge: items from multiple sources unioned.
- Dedupe (task 5): same canonical URL collapses across case-of-scheme/host, trailing slash, fragment; **distinct query strings kept**; deterministic representative = smallest `Id` (kept item asserted, run twice equal).
- Ordering (task 6): publish-date descending; null `PublishedAt` last; equal dates → `Id` ordinal tie-break; deterministic across two runs.
- Edge (task 4): no sources → empty; source yielding nothing → empty; result is read-only (`((ICollection<NewsItem>)result).IsReadOnly`).
- Failure (task 7): a throwing source propagates (fail-fast); pre-cancelled token → `OperationCanceledException`.

**DigestApplicationServiceOrchestrationTests** (task 2, 4, 7):
- Order: shared call-log == `["collect","enrich","compose","cache"]`.
- Wiring: enrichment receives collected items; editorial receives enriched items; returned digest is editorial's output; cache received that digest under the service's key (`SetCount==1`).
- Progress (task 4): `RecordingProgress` captures stages incl. `collecting`/`enriching`/`composing`/`done`; null progress → no throw.
- Failure (task 7): ctor throws `ArgumentNullException` for each null port.

**Domain validation** (task 3, 4, 7):
- `NewsItemTests`: blank Id/Title/Source rejected (`ThrowsAny<ArgumentException>` — covers null+empty+whitespace); null Url → `ArgumentNullException`; relative Url → `ArgumentException`; null Content/PublishedAt allowed; `with { Title = "" }` re-validates (invariant survives copy).
- `DigestTests`: default `GeneratedAt` rejected; empty Sections allowed; null Sections → ANE; `[null]` section entry rejected; `DigestSection` blank Category rejected; `[null]` item entry rejected.
- `EnrichedItemTests`: null Item → ANE; blank Summary/Category rejected; tag entry null/blank rejected; defaults assert `Tags` empty and `Relevance == RelevanceScore.Zero`.

## Key patterns

Item factory per test class to keep cases terse and valid under the new invariants:
```csharp
private static NewsItem Item(string id, string url, DateTimeOffset? published = null)
    => new() { Id = id, Title = "t", Url = new Uri(url), Source = "src", PublishedAt = published };
```
Throw assertions: `ThrowsAny<ArgumentException>` for "invalid string" cases (covers `ArgumentNullException`
subclass from `ArgumentException.ThrowIfNullOrWhiteSpace(null)`); `Throws<ArgumentNullException>` for null refs.
Order assertion via a single shared `List<string>` log passed to all four application fakes.
`AgentProgress` lives in `NewsAggregator.Core.Application`.

## Verification

`global.json` pins SDK `10.0.300` (only `10.0.100` installed) → run from a neutral cwd so the pin is
bypassed (uses installed GA SDK, supports net10.0 + C# 14):
```bash
cd /tmp && dotnet test .../NewsAggregator.Tests/NewsAggregator.Tests.csproj
```

## Outcome

Implemented as planned. One self-inflicted miss on first run: two wiring asserts used `Assert.Same`,
but the application fakes `.ToList()`-copy their inputs, so identity differed though content matched —
switched those two to `Assert.Equal` (value equality, more behaviour-oriented). Final: **74 passed,
0 failed** (9 pre-existing + 65 new). Committed as `2200e6d` on branch `feat/core-business-logic`.
