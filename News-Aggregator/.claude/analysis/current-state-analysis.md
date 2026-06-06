# News-Aggregator — Current State Analysis & Completion Plan

> **Date:** 2026-06-05 · **Branch:** `feat/p3-sequential-editorial-workflow` (post-P3 state)
> **Purpose:** Establish exactly how far the implementation is against the MVP defined in
> `News-Aggregator/docs/`, then decompose the remaining work into independently-buildable,
> numbered prompts. The prompts themselves live in
> [`../prompts/mvp-completion-prompts.md`](../prompts/mvp-completion-prompts.md).
>
> This document is **descriptive of the code as it actually is** (every claim below was read
> from source, not assumed). The MVP contract is `docs/01`–`docs/07`; the "Definition of Done"
> is `docs/07-solid-tradeoffs-and-roadmap.md §7.5`.

---

## 1. Method

I read all seven `docs/*` MVP chapters, both `.claude/analysis/*` session logs, all five
`.claude/plans/*`, and the actual source of every non-trivial file in `src/`. Every "done /
not-done" call below maps to a specific file and was verified by reading it. SDK `10.0.300` on
PATH at `~/.dotnet`. Test-count timeline: **119** (pre-P1 baseline) → **143** (P1) → **150** (P2)
→ **164** (P3, current). Builds 0 warn / 0 err (Infrastructure + Web).

---

## 2. Scope decisions taken with the user (2026-06-05)

| # | Question | Decision |
|---|----------|----------|
| 1 | How far should the prompt plan reach? | **MVP Definition-of-Done only.** Post-MVP roadmap (Redis, persistence, durable workflows, HITL, more orchestration patterns) is explicitly **out** of these prompts and listed in §6 for reference. |
| 2 | `GitHubNewsSource` exists in code but the MVP docs (§1.2/§1.5) name only Hacker News + RSS. | **Keep it; document the deviation.** It is built and tested (16 tests). The remaining pipeline is **source-count-agnostic** (it consumes the deduped `NewsItem[]` and groups by LLM category, not by source), so no prompt special-cases it. |
| 3 | How independent must each prompt be? | **Sequential DAG, green at every step.** Each prompt is self-contained (executable from its text alone), declares its prerequisites, and must leave the build + full test suite green. |

> **Deviation register (constraint D — "do not deviate from the MVP in docs"):**
> The only material deviation already present is the **third source (`GitHubNewsSource`)**. Per
> decision #2 it is retained. No new deviation is introduced by the completion prompts — every
> prompt implements something the docs already specify.

---

## 3. What is DONE (verified against source)

Last recorded test run: **164 passed, 0 failed** (P3, current; timeline: 119 pre-P1 → 143 P1 → 150 P2 → 164 P3).

### 3.1 Solution & build plumbing — ✅ complete
- Five projects exactly (`AppHost`, `Web`, `Core`, `Infrastructure`, `Tests`) — matches docs §2.1.
  `ServiceDefaults` correctly folded into `Web/Extensions/ServiceDefaultsExtensions.cs` (docs §2.1 tradeoff).
- `src/Directory.Build.props`, `src/Directory.Packages.props` (Central Package Management),
  `global.json` pinning SDK **10.0.300**, `NewsAggregator.slnx`.

### 3.2 Core (`NewsAggregator.Core`) — ✅ complete, BCL-only
- **Domain:** `NewsItem`, `EnrichedItem`, `Digest`/`DigestSection`, `RelevanceScore`,
  `ChatModelDescriptor`/`ModelProvider`, `AgentRole` — all with construction-time validation
  (C# 14 `field` keyword pattern).
- **Application:** `NewsAggregationService.CollectAsync` (concurrent drain → dedupe by canonical
  URL **or** normalized title → publish-date-desc sort) and `DigestApplicationService`
  (orchestrates collect → enrich → compose → cache, reports `AgentProgress`).
- **Application/Enrichment (P1):** `Taxonomy` (closed category set + `Normalize`),
  `CategoryResult`/`RelevanceResult` POCOs, and the pure, **total** `EnrichedItemAssembler`
  (raw agent replies → valid `EnrichedItem`, never throws). BCL-only (`System.Text.Json` only).
- **Application/Editorial (P3):** `DigestComposer` (pure, BCL-only, total — sort by
  `Relevance.Value` desc + stable title ordinal tie-break, `GroupBy(Category)` with first-appearance
  section rank, returns empty on empty input) and `EditorIntroParser` (pure, BCL-only, total —
  extracts first balanced `{…}` block → category→intro map, OrdinalIgnoreCase, never throws on
  fences/prose/garbage). Both are `System.Text.Json` + Core.Domain only; `DependencyRuleTests`
  still green.
- **Ports:** `INewsSource`, `INewsAggregationService`, `IEnrichmentWorkflow`,
  `IEditorialWorkflow`, `IChatModelProvider`, `IDigestCache`, `IDigestApplicationService`.
- `DependencyRuleTests` enforces Core has **no** framework/provider references.

### 3.3 Infrastructure (`NewsAggregator.Infrastructure`) — ✅ workflows complete
- **Sources:** `HackerNewsSource`, `RssNewsSource`, `GitHubNewsSource` — all implemented + tested
  (offline via `FakeHttpMessageHandler`; bounded concurrency; error isolation; fail-fast on caller cancel).
- **Model providers:** `OllamaChatModelProvider`, `OpenRouterChatModelProvider` (per-agent model
  override → default fallback), `ChatClientFactory` (provider switch → `IChatClient` pipeline
  `.AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build(sp)`), `IChatClientFactory`.
- **Agents:** `AgentFrameworkAgentFactory` now caches one `AIAgent` per role via
  `ConcurrentDictionary<AgentRole,Lazy<AIAgent>>` — stops a per-refresh `IChatClient`/HTTP leak
  (fix 37d71f9). `AgentInstructions` — Summarizer/Categorizer/Ranker emit the P1 contract (plain
  text / strict JSON, taxonomy sourced from `Taxonomy.Categories`); Editor prompt refined in P3 to
  strict minified JSON (object category→intro).
- **Workflows (P2):** `ConcurrentEnrichmentWorkflow.EnrichAsync` implemented — per-article fan-out
  Summarizer∥Categorizer∥Ranker via `AgentWorkflowBuilder.BuildConcurrent`; merge via
  `EnrichedItemAssembler`; `TurnToken(emitEvents:true)` + `WatchStreamAsync`, updates routed by
  `AIAgent.Id`; `AgentProgress{Stage="enriching"}`; cross-article parallelism bounded by
  `SemaphoreSlim(MaxDegreeOfParallelism)` + `Task.WhenAll` (input order preserved); cancellation
  rethrown.
- **Workflows (P3):** `SequentialEditorialWorkflow.ComposeAsync` implemented — `DigestComposer` →
  if 0 sections return empty `Digest` (skip LLM) → else Editor agent via
  `AgentWorkflowBuilder.BuildSequential([editor])` + `RunStreamingAsync` +
  `TurnToken(emitEvents:true)` + `WatchStreamAsync` (route by `editor.Id`),
  `AgentProgress{Role=Editor,Stage="composing"}`, then `ThrowIfCancellationRequested` →
  `EditorIntroParser.Parse` → map intros by category → `Digest{GeneratedAt=_clock.GetUtcNow(),
  Sections}`. `TimeProvider` injected into ctor; `TryAddSingleton(TimeProvider.System)` registered
  in `InfrastructureServiceCollectionExtensions` (additive — no `Web/Program.cs` change).
- **Caching:** `InMemoryDigestCache`.
- **DI:** `InfrastructureServiceCollectionExtensions` wires all of the above (named HTTP clients,
  provider selection from config, agents, workflows, cache, `TimeProvider.System`).

### 3.4 Web (`NewsAggregator.Web`) — ⚠️ scaffold + partial
- `Program.cs` is the single composition root: Options binding with `ValidateOnStart`
  (incl. the "OpenRouter key required when provider == OpenRouter" rule), Core service
  registration, `AddInfrastructure()`, Blazor Server, `AddServiceDefaults()`/`MapDefaultEndpoints()`.
- Blazor scaffold present (`App`, `Routes`, `MainLayout`, `Home`, `Error`, `Digest`).

### 3.5 AppHost (`NewsAggregator.AppHost`) — ⚠️ partial
- Wires `webfrontend` + an `ollama` generic container with a persistent volume, injects
  `Models__Ollama__Endpoint`, `WaitFor(ollama)`. Redis deferred (commented, post-MVP).

### 3.6 Tests (`NewsAggregator.Tests`) — ⚠️ complete for what exists
- Domain, application-orchestration, all three source adapters, and the P1
  `EnrichedItemAssembler` covered (`Tests/Application/EnrichedItemAssemblerTests.cs`).
- **P2 additions:** `Tests/Fakes/FakeAgentFactory.cs` + `Tests/Workflows/ConcurrentEnrichmentWorkflowTests.cs`
  (7 tests).
- **P3 additions:** `Tests/Application/DigestComposerTests.cs` (6 tests),
  `Tests/Workflows/SequentialEditorialWorkflowTests.cs` (8 tests),
  `Tests/Fakes/FixedTimeProvider.cs` (local BCL `TimeProvider` fake; chosen over
  `Microsoft.Extensions.TimeProvider.Testing` to keep the lean MVP dependency set).
- `FakeChatClient` (deterministic `IChatClient`) and `FakeHttpMessageHandler` in use across all
  workflow tests. **No UI tests yet** (P4 scope).

---

## 4. What is NOT DONE — the gap to MVP Definition-of-Done

Each gap below cites the file and the doc clause it satisfies. These map 1:1 onto the prompts.

| # | Gap | Evidence (file) | Doc clause | Prompt |
|---|-----|-----------------|------------|--------|
| ~~G1~~ | ✅ **DONE (P1).** `Core/Application/Enrichment/EnrichedItemAssembler.cs` + `Taxonomy` + `CategoryResult`/`RelevanceResult` — pure, total mapper from three agent replies to a valid `EnrichedItem`. | `Core/Application/Enrichment/*` | §3.3 ("small Core mapper … framework-free") | **P1** ✅ |
| ~~G2~~ | ✅ **DONE (P1 + P3).** Enrichment prompts (Summarizer/Categorizer/Ranker) done in P1; Editor prompt refined in P3 to strict minified JSON (category→intro). | `Agents/AgentInstructions.cs` | §3.2, §3.3 | **P1** ✅ + **P3** ✅ |
| ~~G3~~ | ✅ **DONE (P2).** `ConcurrentEnrichmentWorkflow.EnrichAsync` implemented — per-article fan-out ∥ via `AgentWorkflowBuilder.BuildConcurrent`; merge via `EnrichedItemAssembler`; bounded parallelism + `Task.WhenAll`; cancellation rethrown. (commits 122e25a + 37d71f9, PR #9) | `Workflows/ConcurrentEnrichmentWorkflow.cs` | §3.3 | **P2** ✅ |
| ~~G4~~ | ✅ **DONE (P3).** `SequentialEditorialWorkflow.ComposeAsync` implemented — `DigestComposer` → Editor agent streaming → `EditorIntroParser` → `Digest`. `TimeProvider` injected. New `DigestComposer` + `EditorIntroParser` in Core (BCL-only). | `Workflows/SequentialEditorialWorkflow.cs` | §3.4 | **P3** ✅ |
| G5 | **UI does not stream live progress, still catches the scaffold exception, and has no category/tag filtering.** | `Components/Pages/Digest.razor` (`catch (NotImplementedException)`) | §1.3 (filter), §1.5/§2.4 (live progress) | **P4** |
| G6 | **No model-provider health check / startup reachability check.** | — (missing) | §5.3, §6.2 | **P5** |
| G7 | **AppHost never makes the Ollama model available**, so a single `dotnet run` cannot actually produce a digest (container starts with no model pulled). | `AppHost/AppHost.cs` | §1.7, §6.1, §7.5 ("single `dotnet run`") | **P6** |
| G8 | **No workflow smoke/integration test** (DoD requires one). | `Tests/` (absent) | §7.5 | **P6** |

### 4.1 Why these are the *complete* set for MVP DoD

Walking `docs/07 §7.5` line by line:

1. *"Five projects … Core references no framework package (CI-checked)"* → **done** (3.1, 3.2).
2. *"One Refresh runs Concurrent enrichment then Sequential editorial and renders a categorized,
   ranked, summarized digest from HN + RSS"* → G1–G4 **done** (P1/P2/P3); G5 (live UI) remains **P4**.
3. *"Provider switch (Ollama ↔ OpenRouter) and adding an RSS feed are config-only"* → **done**
   (3.3 providers + config); a health check (G6) makes the active provider's status observable (**P5**).
4. *"`dotnet run` on AppHost starts Web + Ollama (+ optional Redis) with the dashboard"* → **G7**
   (the model must be present for the run to be meaningful) remains **P6**.
5. *"Tests: Core unit tests, adapter tests with fakes, one workflow smoke test green"* → core/adapter
   + workflow unit tests **done** (164 passing); the end-to-end smoke test is **G8** (**P6**).
6. *"Every Agent Framework/Aspire package version pinned and aligned"* → **done** (3.1).

Remaining gap to MVP DoD: **P4** (Blazor UI), **P5** (health check — independent), **P6** (AppHost
model bootstrap + end-to-end smoke test). After P6, §7.5 is fully satisfied.

---

## 5. Sequencing rationale (the DAG)

```
P1 ✅ (enrichment contract + Core mapper + enrichment prompts)
 ├─> P2 ✅ (Concurrent enrichment workflow)
 └─> P3 ✅ (Sequential editorial workflow)     P5 (provider health check)  ── independent
            P2 ✅, P3 ✅ ──> P4 (Blazor: progress + real refresh + filtering)
            P2 ✅, P3 ✅ ──> P6 (AppHost model bootstrap + end-to-end smoke test)
```

- **P1 ✅ done** — enrichment contract, Core mapper, enrichment prompts.
- **P2 ✅ done** — `ConcurrentEnrichmentWorkflow` implemented (commits 122e25a + 37d71f9, PR #9).
- **P3 ✅ done** — `SequentialEditorialWorkflow` implemented; `DigestComposer` + `EditorIntroParser`
  added to Core; Editor prompt refined; `TimeProvider` injected.
- **P4 and P6 depend on P2+P3** because they need an end-to-end digest to render / smoke-test.
- **P5 (health check) is fully independent** — it can be built at any time.

Each prompt's "Definition of Done" requires the **full** suite (existing + new) to stay green, so
the system never regresses between steps and we avoid an enhancement/bug-fix loop (constraint F).

---

## 6. Explicitly NOT in these prompts (post-MVP — for reference only)

From `docs/07 §7.4`, deferred and **not** covered by P1–P6: Redis `DistributedDigestCache`;
`IArticleRepository` + EF/SQLite persistence; durable/checkpointed workflows; Handoff / Group Chat
/ Magentic orchestrations; human-in-the-loop approval gates; embeddings / semantic dedupe; auth &
personalization; container/Kubernetes deployment & manifest. The `Cache` option and the commented
Redis lines in `AppHost.cs` are left as-is.

---

## 7. Cross-cutting constraints every prompt must honour

These come from `docs/README` "Design principles (non-negotiable)" and the user's constraints; each
prompt repeats the relevant ones, but they apply globally:

- **Core stays BCL-only** — adding any package to Core breaks `DependencyRuleTests` by design.
- **DI only, single composition root** (`Web/Program.cs`) — no static service locator,
  no `Activator`/`ServiceLocator`.
- **No business logic in the UI** — Blazor calls application services and renders only.
- **No new abstractions or signature changes** unless the prompt explicitly scopes them (changing a
  Core port signature forces a Web composition-root edit — keep changes additive).
- **Accuracy policy** (docs README): any Agent Framework / Aspire / OpenAI API whose exact
  signature can drift between versions is marked ⚠️ and **must be verified against the installed
  package** before coding. The authoritative versions are `src/Directory.Packages.props` +
  `src/global.json` (**not** the `docs/` chapters, which pinned older 1.8.0/13.4.0 values the code
  corrected): Agent Framework `1.9.0`, `Microsoft.Extensions.AI` `10.6.0`,
  `Aspire.Hosting.AppHost`/`Aspire.AppHost.Sdk` `13.4.2`, `OllamaSharp` `5.4.25`, `OpenAI` `2.10.0`.
  **No hallucinated APIs.**
- **Determinism** — all non-LLM logic (parsing, sort, group, filter) is pure and unit-tested with
  fakes (`FakeChatClient`, `FakeHttpMessageHandler`); no live network/model calls in tests.
- **SDK pin** — build/test with SDK `10.0.300`. AppHost must be built with the real SDK (needs the
  `Aspire.AppHost.Sdk` msbuild SDK from `global.json`), not from a neutral cwd.

---

## 8. Pointers

- **Prompts:** [`../prompts/mvp-completion-prompts.md`](../prompts/mvp-completion-prompts.md)
- **MVP contract:** `News-Aggregator/docs/01`…`07`
- **Prior session logs:** `session.md`, `implementation-summary.md`
- **Prior plans:** `plans/01`…`05`
</content>
</invoke>
