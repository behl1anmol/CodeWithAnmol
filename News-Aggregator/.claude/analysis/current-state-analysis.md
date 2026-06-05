# News-Aggregator — Current State Analysis & Completion Plan

> **Date:** 2026-06-05 · **Branch:** `claude/news-aggregator-analysis-MrbTS`
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
not-done" call below maps to a specific file and was verified by reading it. No build was run in
this session (the container has no .NET SDK; prior sessions installed `10.0.300` to `/tmp` — see
`session.md`), so test counts are quoted from the last recorded run (plan 05: **119 passed**),
not re-measured.

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

Last recorded test run: **119 passed, 0 failed** (`.claude/plans/05-github-source.md`).

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
- **Ports:** `INewsSource`, `INewsAggregationService`, `IEnrichmentWorkflow`,
  `IEditorialWorkflow`, `IChatModelProvider`, `IDigestCache`, `IDigestApplicationService`.
- `DependencyRuleTests` enforces Core has **no** framework/provider references.

### 3.3 Infrastructure (`NewsAggregator.Infrastructure`) — ⚠️ mostly complete
- **Sources:** `HackerNewsSource`, `RssNewsSource`, `GitHubNewsSource` — all implemented + tested
  (offline via `FakeHttpMessageHandler`; bounded concurrency; error isolation; fail-fast on caller cancel).
- **Model providers:** `OllamaChatModelProvider`, `OpenRouterChatModelProvider` (per-agent model
  override → default fallback), `ChatClientFactory` (provider switch → `IChatClient` pipeline
  `.AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build(sp)`), `IChatClientFactory`.
- **Agents:** `AgentFrameworkAgentFactory` builds a `ChatClientAgent` per role.
  `AgentInstructions` holds **placeholder** prompts (no structured-output contract yet).
- **Caching:** `InMemoryDigestCache`.
- **DI:** `InfrastructureServiceCollectionExtensions` wires all of the above (named HTTP clients,
  provider selection from config, agents, workflows, cache).

### 3.4 Web (`NewsAggregator.Web`) — ⚠️ scaffold + partial
- `Program.cs` is the single composition root: Options binding with `ValidateOnStart`
  (incl. the "OpenRouter key required when provider == OpenRouter" rule), Core service
  registration, `AddInfrastructure()`, Blazor Server, `AddServiceDefaults()`/`MapDefaultEndpoints()`.
- Blazor scaffold present (`App`, `Routes`, `MainLayout`, `Home`, `Error`, `Digest`).

### 3.5 AppHost (`NewsAggregator.AppHost`) — ⚠️ partial
- Wires `webfrontend` + an `ollama` generic container with a persistent volume, injects
  `Models__Ollama__Endpoint`, `WaitFor(ollama)`. Redis deferred (commented, post-MVP).

### 3.6 Tests (`NewsAggregator.Tests`) — ⚠️ complete for what exists
- Domain, application-orchestration, and all three source adapters covered.
- `FakeChatClient` (deterministic `IChatClient`) and `FakeHttpMessageHandler` exist and are ready
  for workflow tests. **No workflow or UI tests yet** (the workflows are not implemented).

---

## 4. What is NOT DONE — the gap to MVP Definition-of-Done

Each gap below cites the file and the doc clause it satisfies. These map 1:1 onto the prompts.

| # | Gap | Evidence (file) | Doc clause | Prompt |
|---|-----|-----------------|------------|--------|
| G1 | **Agent-output → `EnrichedItem` mapping** is undefined. The aggregator that turns three agent replies into one `EnrichedItem` (summary / category+tags / score+reason) does not exist, and there is no parsing contract. | — (missing) | §3.3 ("small Core mapper … framework-free") | **P1** |
| G2 | **Agent prompts are placeholders** with no structured-output contract for Categorizer/Ranker. | `Agents/AgentInstructions.cs` (`TODO(scaffold)`) | §3.2, §3.3 | **P1** (enrichment), **P3** (Editor) |
| G3 | **Concurrent enrichment workflow not implemented.** | `Workflows/ConcurrentEnrichmentWorkflow.cs` → `throw new NotImplementedException` | §3.3 | **P2** |
| G4 | **Sequential editorial workflow not implemented.** | `Workflows/SequentialEditorialWorkflow.cs` → `throw new NotImplementedException` | §3.4 | **P3** |
| G5 | **UI does not stream live progress, still catches the scaffold exception, and has no category/tag filtering.** | `Components/Pages/Digest.razor` (`catch (NotImplementedException)`) | §1.3 (filter), §1.5/§2.4 (live progress) | **P4** |
| G6 | **No model-provider health check / startup reachability check.** | — (missing) | §5.3, §6.2 | **P5** |
| G7 | **AppHost never makes the Ollama model available**, so a single `dotnet run` cannot actually produce a digest (container starts with no model pulled). | `AppHost/AppHost.cs` | §1.7, §6.1, §7.5 ("single `dotnet run`") | **P6** |
| G8 | **No workflow smoke/integration test** (DoD requires one). | `Tests/` (absent) | §7.5 | **P6** |

### 4.1 Why these are the *complete* set for MVP DoD

Walking `docs/07 §7.5` line by line:

1. *"Five projects … Core references no framework package (CI-checked)"* → **already done** (3.1, 3.2).
2. *"One Refresh runs Concurrent enrichment then Sequential editorial and renders a categorized,
   ranked, summarized digest from HN + RSS"* → **G1–G5**.
3. *"Provider switch (Ollama ↔ OpenRouter) and adding an RSS feed are config-only"* → **already done**
   (3.3 providers + config); a health check (G6) makes the active provider's status observable.
4. *"`dotnet run` on AppHost starts Web + Ollama (+ optional Redis) with the dashboard"* → **G7**
   (the model must be present for the run to be meaningful).
5. *"Tests: Core unit tests, adapter tests with fakes, one workflow smoke test green"* → core/adapter
   tests **done**; the workflow smoke test is **G8**.
6. *"Every Agent Framework/Aspire package version pinned and aligned"* → **already done** (3.1).

No other DoD clause is unmet, so **P1–P6 are exhaustive** for the MVP.

---

## 5. Sequencing rationale (the DAG)

```
P1 (enrichment contract + Core mapper + enrichment prompts)
 ├─> P2 (Concurrent enrichment workflow)
 └─> P3 (Sequential editorial workflow)        P5 (provider health check)  ── independent
            P2, P3 ──> P4 (Blazor: progress + real refresh + filtering)
            P2, P3 ──> P6 (AppHost model bootstrap + end-to-end smoke test)
```

- **P1 first** because both workflows (P2, P3) and the smoke test (P6) need a single, tested
  definition of how raw agent output becomes domain data. Putting it in Core (pure, BCL-only) keeps
  it framework-free and unit-testable without a live model — directly per docs §3.3.
- **P2 and P3 are siblings** (both depend only on P1; `ComposeAsync` consumes `EnrichedItem[]`,
  which already exists in Core). They can be built in either order.
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
  package** (Agent Framework `1.8.0`, `Microsoft.Extensions.AI` `10.6.0`, Aspire `13.4.0`,
  `OllamaSharp` v4+) before coding. **No hallucinated APIs.**
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
