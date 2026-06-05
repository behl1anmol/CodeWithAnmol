---
name: news-aggregator-dev
description: >-
  Engineering playbook for the News-Aggregator .NET 10 / Aspire / Microsoft Agent
  Framework repo. Use this WHENEVER you build, modify, test, or review code in this
  repository — especially the MVP completion prompts (P1–P6 in
  .claude/prompts/mvp-completion-prompts.md), the concurrent/sequential agent workflows,
  the agent factory, model providers, sources, caching, health checks, the Aspire AppHost,
  or anything touching Microsoft.Agents.AI(.Workflows) / Microsoft.Extensions.AI / OllamaSharp.
  It carries the verified build/test setup, the architecture invariants that the test suite
  enforces, the house code style, and — critically — the EMPIRICALLY VERIFIED behaviour of
  the Agent Framework workflow APIs (TurnToken, routing, disposal) that the docs get wrong.
  Read it before writing code so you don't hallucinate an API or break a dependency rule.
---

# News-Aggregator — Engineering Playbook

A local-first tech-news digest app: collect articles → **concurrently** enrich each
(Summarizer ∥ Categorizer ∥ Ranker) → **sequentially** compose an editorial digest →
show it in Blazor. Built on **.NET 10**, **.NET Aspire**, and the **Microsoft Agent
Framework**, with a strict Clean-Architecture boundary that the test suite enforces.

This skill is the shared memory for working in this repo. Skim the whole file; then **read
the matching reference before touching that area** — the reference files hold the verified
API details that prevent the runtime bug/fix loops the architecture is designed to avoid.

| You are about to… | Read first |
|---|---|
| Touch any agent **workflow** (`ConcurrentEnrichmentWorkflow`, `SequentialEditorialWorkflow`) or the agent factory | `references/agent-framework-1.9.md` |
| Use a new method/type from **any** SDK (Agent Framework, Aspire, Extensions.AI, OllamaSharp) | `references/verifying-apis.md` |

---

## 1. The contract & the work queue

- **Authoritative spec:** `docs/01`…`docs/07`. The MVP Definition of Done is **`docs/07 §7.5`**.
- **Work is sliced into self-contained prompts:** `.claude/prompts/mvp-completion-prompts.md`
  defines **P1–P6**, each stating its own goal, file scope, steps, and a verifiable
  Definition of Done. To build one, you only need its listed prerequisites merged.
  - Dependency graph: `P1 → {P2, P3} → {P4, P6}`; `P5` independent.
  - `P1` enrichment contract+assembler · `P2` concurrent enrichment · `P3` sequential
    editorial · `P4` Blazor UI · `P5` provider health check · `P6` AppHost model bootstrap
    + end-to-end smoke test.
- **Prior context:** `.claude/analysis/` (state analysis) and `.claude/plans/` (per-prompt
  plans + the baseline test count). Read the relevant one before starting a prompt.

**Version source of truth is the repo, not the docs.** The `docs/05` chapter pinned older
package versions; the code corrected them. Always re-read `src/Directory.Packages.props`
(+ `src/global.json` for the Aspire AppHost SDK) for the real pins before writing code.

---

## 2. Build & test (do this exactly)

The **.NET 10.0.300 SDK is installed and on PATH** at `~/.dotnet/dotnet` (`global.json`
pins `10.0.300`, `rollForward: latestFeature`). Don't trust older memory notes claiming you
must install it under `/tmp` — verify with `dotnet --list-sdks` first.

```bash
export PATH=$HOME/.dotnet:$PATH
cd src

dotnet build NewsAggregator.Infrastructure/NewsAggregator.Infrastructure.csproj   # expect 0/0
dotnet build NewsAggregator.Web/NewsAggregator.Web.csproj                         # expect 0/0
dotnet test  NewsAggregator.Tests/NewsAggregator.Tests.csproj                     # full suite
```

- **Definition of Done for every change:** compiles with **0 warnings / 0 errors** AND the
  **entire** test suite passes. Never leave the tree red.
- The test count must be **monotonically non-decreasing** — add tests, break none. (Recent
  high-water mark: 150 passed after P2; 119 was the original P1-era baseline.)
- **AppHost is special:** `dotnet build NewsAggregator.AppHost/...` needs the **real SDK on
  PATH** (it consumes the `Aspire.AppHost.Sdk` msbuild SDK from `global.json`). Do **not**
  build it from a neutral cwd.
- Tests are **deterministic and offline** — no live Ollama/OpenRouter, no network. Use the
  existing fakes (see §4). If a test needs timing/concurrency, drive it with injected delays,
  not `Thread.Sleep` guesses; assert bounds, not exact schedules.

---

## 3. Architecture invariants (the test suite will fail you otherwise)

These are enforced by `DependencyRuleTests` and the repo's design rules. Breaking one is a
real failure, not a style nit.

1. **Core is BCL-only.** Never add a NuGet package to `NewsAggregator.Core`. `System.Text.Json`
   is fine (ships in the BCL). Framework/SDK types (`Microsoft.Agents.AI*`,
   `Microsoft.Extensions.AI`, OllamaSharp, OpenAI, Aspire) live **only** in
   `Infrastructure` / `Web`. The `IAgentFactory` port returns the framework type `AIAgent`,
   so it lives in **Infrastructure**, not Core — Core sees only the workflow ports
   (`IEnrichmentWorkflow`, `IEditorialWorkflow`).
2. **Single composition root.** DI is wired only in `Web/Program.cs` /
   `InfrastructureServiceCollectionExtensions`. No service locator, no `Activator`.
3. **No business logic in the UI.** Blazor components call application services and render;
   non-trivial logic goes into a pure, unit-tested helper.
4. **Additive changes only** unless the prompt explicitly scopes a signature change. Changing
   a Core port signature forces a composition-root edit — avoid it. Stay inside the prompt's
   declared file scope; if correctness seems to require a file outside it, prefer an in-scope
   solution and call out the tradeoff (see the P2 routing decision in
   `references/agent-framework-1.9.md` for a worked example).
5. **Determinism lives in Core; the LLM lives in Infrastructure.** Sort/group/parse/merge are
   pure Core helpers (e.g. `EnrichedItemAssembler`, `Taxonomy`) so they're testable without an
   agent. Keep parsing OUT of the framework aggregator delegate.
6. **Total parsers never throw.** `EnrichedItemAssembler` is deliberately total: any junk LLM
   output still yields a valid `EnrichedItem`. Preserve that property — it's what keeps the
   workflows free of runtime parse/construction failures.

---

## 4. House style & test fakes

**Code style** (match the surrounding files):
- File-scoped namespaces, `Nullable` enabled, `ImplicitUsings` enabled, `LangVersion latest`.
- Collection expressions (`[.. items.Select(...)]`, `[]`) and target-typed `new`.
- **No `ConfigureAwait(false)`** anywhere — the codebase deliberately omits it; match that.
- Bounded fan-out pattern (copy it from `Sources/RssNewsSource.cs` /
  `Sources/HackerNewsSource.cs`): `using var gate = new SemaphoreSlim(Math.Max(1, max));`
  then `Task<T>[] tasks = [.. items.Select(i => DoAsync(i, gate, ct))];` then
  `await Task.WhenAll(tasks)` (which **preserves input order**); acquire inside the task with
  `await gate.WaitAsync(ct)` and release in a `finally`.
- Progress is **null-safe**: `progress?.Report(...)`.
- Write XML doc comments that explain **why**, not just what — especially for any non-obvious
  framework interaction (cite the verified behaviour).

**Existing test fakes** (in `NewsAggregator.Tests/Fakes/`) — reuse, don't reinvent:
- `FakeChatClient` — deterministic `IChatClient` returning one canned reply (streaming + not).
- `FakeAgentFactory` — `IAgentFactory` backing each role with a canned reply; optional
  `clientFactory` hook to substitute the backing `IChatClient` (e.g. to observe concurrency).
- `FakeNewsSource` — deterministic `INewsSource`, honours cancellation, can throw to drive
  fail-fast.
- `FakeHttpMessageHandler` — for source/health-check HTTP without network.
- `RecordingProgress<T>` — synchronous `IProgress<T>` that records every report (don't use
  `Progress<T>` in tests; its sync-context posting is async and flaky).

---

## 5. The golden rule: verify SDK APIs against the *installed* package

The single biggest source of bugs here is assuming an SDK method's name, signature, or
runtime behaviour. The Agent Framework in particular behaves in ways the docs and IntelliSense
do **not** make obvious (a workflow silently hangs `Idle` if you skip a `TurnToken`).

**Before using any unfamiliar SDK API: confirm it empirically.** The method — grep the
installed package's XML docs, then write a throwaway console probe in `/tmp` pinned to the
exact versions and *observe* the behaviour — is documented in
**`references/verifying-apis.md`**. It is cheap (a few minutes) and it is what produced every
verified fact in `references/agent-framework-1.9.md`. Use it; don't guess.
