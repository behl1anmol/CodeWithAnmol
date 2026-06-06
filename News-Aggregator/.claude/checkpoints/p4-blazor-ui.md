# Checkpoint — P4: Blazor UI (live progress, real refresh, category/tag filter)

**Prompt:** P4 (`../prompts/mvp-completion-prompts.md`) · **Prereq:** P2 ✅ + P3 ✅ ·
**Result:** 195 passed, 0 failed; Core + Web build 0 warn / 0 err (after PR #11 review follow-up).
**Branch:** `feat/p3-sequential-editorial-workflow`.

## What it does
`Digest.razor` now drives a **real** refresh end-to-end, streams **live agent progress** to the
browser, and **filters** the rendered digest by category and/or tag — delivering user stories §1.3
and the "watch agents process items live" story (§1.5/§2.4). The scaffold
`catch (NotImplementedException)` is gone. All filtering logic lives in a pure Core helper, not in
the component (docs §2.6).

## Files
| File | Change |
|---|---|
| `Core/Application/Editorial/DigestFilter.cs` | **new** — pure, BCL-only. `Apply(digest, category?, tag?)`: AND filter, case-insensitive (`OrdinalIgnoreCase`); blank/null arg = no constraint; neither set → returns the same instance unchanged; tag filter rebuilds sections via `section with { Items = [..] }` and drops emptied sections; **preserves `GeneratedAt`** so an empty result is still a valid `Digest`. `DistinctTags(digest)`: distinct tags across all items (`Distinct(OrdinalIgnoreCase)` → `OrderBy(Ordinal)`) for the tag picker. |
| `Web/Components/Pages/Digest.razor` | **modified** — see flow below. |
| `Tests/Application/DigestFilterTests.cs` | **new** — 10 tests (no UI): no-filter passthrough (`Assert.Same`), blank-as-no-constraint, category filter, category case-insensitive, tag filter drops emptied sections, tag case-insensitive, category+tag AND, unmatched → empty sections + timestamp preserved, `DistinctTags` dedupe+order, `DistinctTags` empty. |

## Digest.razor wiring
1. **Live progress:** `var progress = new Progress<AgentProgress>(p => { _progress = p; _ = InvokeAsync(StateHasChanged); });` passed to `DigestService.RefreshDigestAsync(progress)`. While `_busy`, renders `Stage` + `ProcessedCount/TotalCount` (e.g. `enriching 3/10`). Blazor Server streams the DOM diff over SignalR — no custom hub (docs §2.4/§7.2-C).
2. **Real refresh / error path:** removed `catch (NotImplementedException)`; now `catch (Exception ex) { _status = "Could not generate the digest…"; Logger.LogError(ex, …); }` with injected `ILogger`. `finally` clears `_busy`.
3. **Filters:** category `<select>` sourced from `Taxonomy.Categories` (P1 constant) + tag `<select>` from `DigestFilter.DistinctTags(_digest)`; both `@bind` to `_category`/`_tag`, "All …" option = empty = no constraint.
4. **View:** computed property `View => _digest is null ? null : DigestFilter.Apply(_digest, _category, _tag)`; markup renders `View` (empty → "No items match the current filter.").
5. **Per item:** title link + relevance (`Relevance.Value.ToString("0.00", InvariantCulture)`) + summary + tags — previously title-only (§1.7).

## Decisions asked this session (constraint B)
- **`DigestFilter` location → Core** (`Application/Editorial/`, beside `DigestComposer`) over Web.
  Rationale: matches the pure-helper pattern, stays BCL-only, unit-tested in
  `NewsAggregator.Tests.Application`, reusable. `DependencyRuleTests` stays green.
- **Filter controls → category + tag** (AND-combined) over category-only, to fully cover §1.3.

## Gotchas hit (fixed)
- **RZ1010**: `@{ var view = … }` inside an `@if {}` body fails (already a code-block body). Fixed by
  moving the computation to the `View` computed property — also keeps the markup declarative.
- **Name clash**: the component class is `Digest`, colliding with `NewsAggregator.Core.Domain.Digest`
  (globally imported via `_Imports.razor`). Followed the existing file's convention — fully-qualify the
  domain type (`_digest`, `View`) and fully-qualify the logger category
  (`ILogger<NewsAggregator.Web.Components.Pages.Digest>`).
- **Unawaited `InvokeAsync`** in the `Progress<T>` callback → explicit `_ = InvokeAsync(…)` discard
  (callback is a synchronous `Action`, so fire-and-forget is intended; discard keeps it warning-clean).

## Verification
- `dotnet build NewsAggregator.Core` → 0/0; `dotnet build NewsAggregator.Web` → 0/0 (SDK 10.0.300).
- `dotnet test NewsAggregator.Tests` → **174 passed, 0 failed** (P3 left 164; +10 DigestFilter tests).
- Reviewed by `cavecrew-reviewer` (caveman plugin); both findings (RZ1010, unawaited task) fixed.
- Manual app run **not** performed — needs a live Ollama/OpenRouter provider; the error path renders a
  friendly message until one is configured. Pure logic fully covered by `DigestFilterTests`.
- `docs/` untouched — P4 changes only UI wiring, no operator instructions or contracts.

## Review follow-up (PR #11)
caveman-review left 5 nits + 1 question; all addressed:
- **`rel="noopener noreferrer"`** on the `target="_blank"` item links (reverse-tabnabbing hygiene).
- **Category picker shows only present categories** — new pure `DigestFilter.PresentCategories(digest)`
  (taxonomy display order, intersected with sections present) replaces the full `Taxonomy.Categories`
  list, so the dropdown never offers a category that renders empty.
- **`DistinctTags` ordering** changed `OrderBy(Ordinal)` → `OrderBy(OrdinalIgnoreCase)` to match the
  case-insensitive dedupe (so `"agents"` sorts before `"CVE"` in the picker).
- **`view` computed once per render** via a top-level `@{ }` block (the `View` computed property,
  which re-ran `Apply` twice per render, was removed).
- **Cancellation** — component now `@implements IDisposable`, owns a `CancellationTokenSource`, passes
  its token to `RefreshDigestAsync`, swallows `OperationCanceledException`, and cancels+disposes in
  `Dispose` so a refresh can't outlive the page.
- Question (tag picker lists all tags regardless of selected category): **deliberate for MVP** — the
  "No items match the current filter." empty state is the signal; coupling the tag list to the selected
  category is deferred.
- Tests: `DistinctTags` ordering test strengthened with a mixed-case tag; +2 `PresentCategories` tests.

## Next
Remaining: **P5** (model-provider health check — independent), **P6** (AppHost Ollama model
bootstrap + end-to-end smoke test — needs P2+P3 ✅). After P6, `docs/07 §7.5` is fully satisfied →
MVP complete.
