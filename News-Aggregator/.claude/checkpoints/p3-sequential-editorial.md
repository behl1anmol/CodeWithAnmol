# Checkpoint — P3: Sequential editorial workflow

**Prompt:** P3 (`../prompts/mvp-completion-prompts.md`) · **Prereq:** P1 ✅ (independent of P2) ·
**Result:** 164 passed, 0 failed; Infrastructure + Web build 0 warn / 0 err.
**Branch:** `feat/p3-sequential-editorial-workflow`.

## What it does
`SequentialEditorialWorkflow.ComposeAsync` is the editorial pipeline of docs §3.4: deterministic
**sort by score desc → group by category** (pure Core), then the **Editor agent** writes one short
intro per section, assembled into the final `Digest`. The only non-deterministic part is the prose
the Editor writes; all structure is a pure, unit-tested function of the input.

## Files
| File | Change |
|---|---|
| `Core/Application/Editorial/DigestComposer.cs` | **new** — pure: `OrderByDescending(Relevance.Value).ThenBy(Item.Title, Ordinal)` (stable) → `GroupBy(Category, Ordinal)` (first-appearance = section rank) → intro-less `DigestSection`s. Empty → empty. Total. |
| `Core/Application/Editorial/EditorIntroParser.cs` | **new** — defensive: first balanced `{…}` block → `category → intro` map (`OrdinalIgnoreCase`), string values only, drops blank key/value. Own brace-matcher. Total — never throws on fences/prose/garbage. |
| `Infrastructure/Workflows/SequentialEditorialWorkflow.cs` | **modified** — replaced `NotImplementedException`; see flow below. |
| `Infrastructure/Agents/AgentInstructions.cs` | **modified** — Editor prompt now demands strict minified JSON, object mapping each exact category name → its intro (no prose, no code fences). |
| `Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` | **modified** — `TryAddSingleton(TimeProvider.System)` (+ `using Microsoft.Extensions.DependencyInjection.Extensions;`). Additive; no `Web/Program.cs` edit. |
| `Tests/Application/DigestComposerTests.cs` | **new** — 6 tests (no agent): item order, equal-score title tie-break, grouping, section order by top item, empty input, intro left null. |
| `Tests/Workflows/SequentialEditorialWorkflowTests.cs` | **new** — 8 tests (fake Editor): intro mapping, case-insensitive map, missing intro → null, junk output → all null (no throw), exact `GeneratedAt`, empty input → empty+timestamped, composing progress, cancellation throws. |
| `Tests/Fakes/FixedTimeProvider.cs` | **new** — minimal BCL `TimeProvider` subclass returning a fixed instant. |

## ComposeAsync flow
1. `ArgumentNullException.ThrowIfNull(enrichedItems)`.
2. `DigestComposer.Compose(enrichedItems)` → deterministic sections (pure Core, no LLM).
3. **0 sections → return `new Digest { GeneratedAt = _clock.GetUtcNow(), Sections = [] }`** — skip the
   Editor agent entirely (nothing to introduce, no reason to touch a model).
4. Else run the Editor: `AgentWorkflowBuilder.BuildSequential([editor])` →
   `InProcessExecution.RunStreamingAsync` → `TurnToken(emitEvents: true)` → `WatchStreamAsync`;
   accumulate `AgentResponseUpdateEvent.Update.Text` where `ExecutorId == editor.Id`; report
   `AgentProgress { Role = Editor, Stage = "composing" }`; then `ct.ThrowIfCancellationRequested()`.
5. `EditorIntroParser.Parse(reply)` → map intros onto sections by category (`section with { Intro }`);
   unknown/missing intro leaves the optional `Intro` null.
6. `new Digest { GeneratedAt = _clock.GetUtcNow(), Sections = sectionsWithIntros }`.

## Decision asked this session (constraint C)
- **`Digest.GeneratedAt` clock → inject `TimeProvider`** (chosen) over `DateTimeOffset.UtcNow`.
  Rationale: deterministic tests assert the exact timestamp via `FixedTimeProvider`; `TimeProvider`
  is BCL so it adds no package; the change is additive (ctor `(IAgentFactory, TimeProvider)` +
  one composition-root registration). Production gets `TimeProvider.System`.

## Verified Agent-Framework behaviour (1.9.0)
- Single-agent `AgentWorkflowBuilder.BuildSequential([editor])` runs and streams exactly like P2's
  `BuildConcurrent` — **runtime-verified** by the passing workflow tests (same `TurnToken` +
  `WatchStreamAsync` + route-by-`Id` pattern). No new API guessed.

## Notes / tradeoffs
- `EditorIntroParser` **duplicates** the brace-matcher from P1's `EnrichedItemAssembler` rather than
  refactor that (tested, out-of-scope) file — kept P3 inside its declared file scope. Candidate to
  hoist into one shared Core JSON helper in a later cleanup.
- `FixedTimeProvider` is a local fake instead of pulling `Microsoft.Extensions.TimeProvider.Testing`
  — keeps the lean MVP dependency set; BCL `TimeProvider` is abstract with `GetUtcNow()` overridable.
- `DependencyRuleTests` still green: `DigestComposer` + `EditorIntroParser` are BCL-only
  (`System.Text.Json` + `Core.Domain`).

## Next
Unblocked: **P4** (Blazor: live progress + real refresh + category/tag filter — needs P2+P3 ✅),
**P5** (provider health check — independent), **P6** (AppHost model bootstrap + e2e smoke test —
needs P2+P3 ✅). After P6, `docs/07 §7.5` is fully satisfied → MVP complete.
