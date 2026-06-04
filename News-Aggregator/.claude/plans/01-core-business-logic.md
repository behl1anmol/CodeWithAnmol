# Plan — Implement NewsAggregator.Core business logic

## Context

Solution is a scaffold. Brief: fill the business logic intentionally left as scaffold,
**only inside `src/NewsAggregator.Core/`**. Do not touch Infrastructure, Web, AppHost, Tests.
Do not add NuGet packages (Core is BCL-only; `DependencyRuleTests` fails the build if a
framework assembly leaks in).

Exploration findings that shape scope:
- Core has **exactly one** `NotImplementedException`: `NewsAggregationService.CollectAsync` (line 21).
- `DigestApplicationService` is **already fully implemented**; its test is green. Only safe
  addition = constructor null-guards (cannot add a clock or new ctor params — that would
  require Web DI changes, which are forbidden).
- `RelevanceScore` **already validates** [0,1] + NaN and its test is green → leave untouched.
- The 3 other domain records (`NewsItem`, `Digest`/`DigestSection`, `EnrichedItem`) have **no
  validation**. Real new work = validation on these + the `CollectAsync` implementation.
- Build: `net10.0`, `LangVersion=latest` (C# 14 → `field` keyword available), `Nullable=enable`,
  `Deterministic=true`.

Locked decisions (confirmed with user):
1. **Dedupe by canonical URL** (normalize scheme+authority case, drop fragment, trim trailing slash; keep path+query).
2. **Fail-fast** on source errors (propagate; resilience/logging is an Infrastructure concern).
3. **init-accessor validation that throws** (value-object style, no call-site changes, consistent with `RelevanceScore`).

## Files modified (5) — RelevanceScore.cs deliberately unchanged

| File | Change | Why |
|------|--------|-----|
| `Application/Services/NewsAggregationService.cs` | Implement `CollectAsync`; add ctor null-guard | Replaces the only `NotImplementedException`; the core merge/dedupe/sort logic |
| `Application/Services/DigestApplicationService.cs` | Add ctor `ArgumentNullException.ThrowIfNull` guards | "Validate inputs"; safe, no signature change, mocks in test stay non-null |
| `Domain/NewsItem.cs` | Validate Id/Title/Source non-blank, Url absolute | Construction-time invariants for raw items |
| `Domain/Digest.cs` | Validate `Digest.GeneratedAt`≠default + Sections non-null/no-null-entries; `DigestSection.Category` non-blank + Items no-null-entries | Output invariants |
| `Domain/EnrichedItem.cs` | Validate Item non-null, Summary/Category non-blank, Tags no null/blank entries | Enriched invariants |

## 1. NewsAggregationService.CollectAsync

Fan-out one task per source (bounded by source count — small: HN+RSS), `Task.WhenAll`,
fail-fast. Order by `Id` (ordinal) **before** dedupe so the surviving representative of a
duplicate group is deterministic under concurrency. Final sort: `PublishedAt` desc, nulls
last, `Id` ordinal tie-break (fully deterministic per `Deterministic=true`). Return an
immutable `ReadOnlyCollection` via `.AsReadOnly()`.

```csharp
public NewsAggregationService(IEnumerable<INewsSource> sources)
{
    ArgumentNullException.ThrowIfNull(sources);
    _sources = sources;
}

public async Task<IReadOnlyList<NewsItem>> CollectAsync(CancellationToken cancellationToken = default)
{
    // Fan-out: drain each source concurrently. Fail-fast — any source exception
    // propagates (resilience/logging is an Infrastructure concern).
    List<NewsItem>[] perSource = await Task.WhenAll(
        _sources.Select(source => DrainAsync(source, cancellationToken)));

    // Order by Id before de-dup so the kept representative is deterministic under
    // concurrent draining, then keep first occurrence per canonical URL.
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var deduped = new List<NewsItem>();
    foreach (NewsItem item in perSource
        .SelectMany(items => items)
        .OrderBy(i => i.Id, StringComparer.Ordinal))
    {
        if (seen.Add(CanonicalKey(item.Url)))
        {
            deduped.Add(item);
        }
    }

    // Sort by publish date descending; nulls last; Id ordinal tie-break (deterministic).
    deduped.Sort(static (a, b) =>
    {
        int byDate = Nullable.Compare(b.PublishedAt, a.PublishedAt);
        return byDate != 0 ? byDate : string.CompareOrdinal(a.Id, b.Id);
    });

    return deduped.AsReadOnly();
}

private static async Task<List<NewsItem>> DrainAsync(INewsSource source, CancellationToken ct)
{
    var items = new List<NewsItem>();
    await foreach (NewsItem item in source.FetchAsync(ct).WithCancellation(ct))
    {
        items.Add(item);
    }
    return items;
}

private static string CanonicalKey(Uri url)
{
    // Same-article key: lowercase scheme+authority, drop fragment, trim trailing
    // slash, keep path+query (query can distinguish distinct articles).
    string scheme = url.Scheme.ToLowerInvariant();
    string authority = url.Authority.ToLowerInvariant();
    string path = url.AbsolutePath.TrimEnd('/');
    return $"{scheme}://{authority}{path}{url.Query}";
}
```

Edge cases: empty `_sources` → `WhenAll([])` → empty list. Source yielding nothing → empty
contribution. Cancellation honored via `WithCancellation`.

## 2. DigestApplicationService — ctor guards only

```csharp
public DigestApplicationService(
    INewsAggregationService aggregation,
    IEnrichmentWorkflow enrichment,
    IEditorialWorkflow editorial,
    IDigestCache cache)
{
    ArgumentNullException.ThrowIfNull(aggregation);
    ArgumentNullException.ThrowIfNull(enrichment);
    ArgumentNullException.ThrowIfNull(editorial);
    ArgumentNullException.ThrowIfNull(cache);

    _aggregation = aggregation;
    _enrichment = enrichment;
    _editorial = editorial;
    _cache = cache;
}
```

No flow change (already collects→enriches→composes→caches, already deterministic — `GeneratedAt`
is set by the editorial workflow in Infrastructure, not Core). No empty-list short-circuit:
that would require a Core-side timestamp = a `TimeProvider` ctor param = a Web DI change (forbidden).

## 3–5. Domain validation (init-accessor, `field` keyword, throws)

Pattern per property (keeps `new(){...}` call sites + `with` working; property initializers
`= []` stay valid):

```csharp
public required string Title
{
    get => field;
    init
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        field = value;
    }
}
```

- **NewsItem**: Id/Title/Source → `ThrowIfNullOrWhiteSpace`; Url → `ThrowIfNull` + `IsAbsoluteUri`
  check (throw `ArgumentException` if relative). Content/PublishedAt untouched.
- **Digest**: `GeneratedAt` → throw if `value == default`; `Sections` → `ThrowIfNull` +
  reject any null entry (empty allowed; keep `= []`).
- **DigestSection**: `Category` → `ThrowIfNullOrWhiteSpace`; `Items` → `ThrowIfNull` + reject
  null entries (empty allowed; keep `= []`); `Intro` untouched.
- **EnrichedItem**: `Item` → `ThrowIfNull`; `Summary`/`Category` → `ThrowIfNullOrWhiteSpace`;
  `Tags` → `ThrowIfNull` + reject `IsNullOrWhiteSpace` entries (empty allowed; keep `= []`);
  `Relevance` keeps `= RelevanceScore.Zero` default.
- **RelevanceScore**: NO change (already validates; test green).

All helpers are BCL (`System`, `System.Linq` via ImplicitUsings) → no package added → Core stays
framework-free. Throwing matches the existing `RelevanceScore` convention.

## Assumptions / rationale

- **No ctor signature changes** on either service → Web's composition root keeps compiling untouched.
- **Existing green tests stay green**: test data (`Id="1"`, `Url=https://example.com`, `Summary="s"`,
  `Category="AI"`, `Digest{GeneratedAt=UnixEpoch}` with empty Sections) all satisfy the new rules.
- `field` keyword is valid on `net10.0` + `LangVersion=latest` (C# 14). Fallback if any build
  surprise: explicit `private readonly` backing fields (same behavior). `TreatWarningsAsErrors=false`
  so nullable-flow warnings can't break the build.
- Dedupe sort-before-dedupe makes results deterministic despite concurrent draining.

## Verification

```bash
cd /tmp && dotnet build .../NewsAggregator.Core.csproj   # 0 warnings, 0 errors
cd /tmp && dotnet test  .../NewsAggregator.Tests.csproj  # all existing tests green
```
(Run from a neutral cwd to bypass the `global.json` SDK pin — 10.0.300 pinned, 10.0.100 installed.)

## Outcome

Implemented exactly as planned. Core built clean (0 warnings/0 errors); all 9 existing tests
green incl. `DependencyRuleTests`. Committed as `3f81e17` on branch `feat/core-business-logic`.

**Later change (`d00ddd0`, PR review P2):** dedup extended from URL-only to **canonical URL OR
normalized title** to match docs §1 ("URL / title hash"). See `analysis/session.md` Request 5.
