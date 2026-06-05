# News-Aggregator — MVP Completion Prompts

> **How to use this file.** In a future session, say *"build Prompt P2 from
> `.claude/prompts/mvp-completion-prompts.md`"* (or any number). Each prompt is **self-contained**:
> it states its own goal, prerequisites, exact file scope, implementation steps, constraints, and a
> verifiable Definition of Done. You do not need to read any other prompt to execute one — only the
> prerequisites listed must already be merged.
>
> **Companion analysis:** [`../analysis/current-state-analysis.md`](../analysis/current-state-analysis.md)
> (what is done, what is not, and why these six prompts are the exhaustive MVP gap).
>
> **MVP contract (do not deviate):** `News-Aggregator/docs/01`…`07`. The Definition of Done is
> `docs/07 §7.5`.

---

## Global rules (apply to every prompt)

Repeat-after-me constraints, enforced in each prompt's Definition of Done:

1. **Core is BCL-only.** Never add a NuGet package to `NewsAggregator.Core` — it breaks
   `DependencyRuleTests` by design. Framework/SDK types live only in `Infrastructure`/`Web`.
2. **Single composition root.** DI is wired only in `Web/Program.cs` /
   `InfrastructureServiceCollectionExtensions`. No static service locator, no `Activator`.
3. **No business logic in the UI.** Blazor components call application services and render; any
   non-trivial logic goes into a pure, unit-tested helper.
4. **Additive changes only** unless the prompt explicitly scopes a signature change. Changing a Core
   port signature forces a Web composition-root edit — avoid it.
5. **No hallucinated APIs (docs accuracy policy).** The **authoritative** version source is the
   repo's `src/Directory.Packages.props` (+ `src/global.json` for the Aspire AppHost SDK) — **not**
   the `docs/` chapters, which pinned older values (`docs/05` says 1.8.0/13.4.0; the code corrected
   them). As pinned today: Agent Framework **1.9.0** (`Microsoft.Agents.AI`, `.Workflows`, `.OpenAI`),
   `Microsoft.Extensions.AI` **10.6.0**, `Aspire.Hosting.AppHost` / `Aspire.AppHost.Sdk` **13.4.2**,
   `OllamaSharp` **5.4.25**, `OpenAI` **2.10.0**. Before writing, re-read `Directory.Packages.props`
   (versions can move again) and verify any method marked ⚠️ against the **installed** package (read
   the assembly / IntelliSense / `microsoft_docs_search`). If a verified signature differs from what's
   shown here, follow the package and note it.
6. **Deterministic, offline tests.** Use `FakeChatClient` (deterministic `IChatClient`) and
   `FakeHttpMessageHandler`. No live model or network calls in tests.
7. **SDK pin = 10.0.300.** Build/test with that SDK. Build the **AppHost with the real SDK on PATH**
   (it needs the `Aspire.AppHost.Sdk` msbuild SDK from `global.json`) — do **not** build it from a
   neutral cwd.
8. **Definition of Done for every prompt:** the change compiles with **0 warnings / 0 errors** and
   the **entire** test suite (existing + new) passes. Never leave the tree red. Ask the user before
   any decision the docs don't already answer (constraint C).

### Dependency graph

```
P1 ──┬──> P2 ──┐
     └──> P3 ──┼──> P4
                └──> P6
P5  (independent)
```

| Prompt | Title | Prerequisites |
|--------|-------|---------------|
| **P1** | Enrichment output contract & Core mapper | none |
| **P2** | Concurrent enrichment workflow | P1 |
| **P3** | Sequential editorial workflow | P1 |
| **P4** | Blazor UI: live progress, real refresh, filtering | P2, P3 |
| **P5** | Model-provider health check & startup validation | none |
| **P6** | AppHost Ollama model bootstrap + end-to-end smoke test | P2, P3 |

---

## P1 — Enrichment output contract & Core mapper

**Goal.** Define one tested, pure, framework-free contract for turning the three enrichment agents'
text replies into a valid `EnrichedItem`, and refine the Summarizer/Categorizer/Ranker prompts to
emit exactly that contract. This is the shared foundation P2 and P6 build on (docs §3.3:
*"the parsing/merge logic lives in a small Core mapper so it stays testable and framework-free"*).

**Prerequisites.** None (Core domain + `AgentInstructions` already exist).

**Files in scope.**
- **New** `src/NewsAggregator.Core/Application/Enrichment/EnrichmentOutputs.cs` — pure POCO(s) for
  the parsed agent results (e.g. `CategoryResult { string Category; IReadOnlyList<string> Tags }`,
  `RelevanceResult { double Score; string? Reason }`). BCL-only.
- **New** `src/NewsAggregator.Core/Application/Enrichment/EnrichedItemAssembler.cs` — a pure mapper:
  given the source `NewsItem` and the three raw agent strings (summary text, categorizer JSON,
  ranker JSON), produce a **valid** `EnrichedItem`.
- **Modify** `src/NewsAggregator.Infrastructure/Agents/AgentInstructions.cs` — refine Summarizer,
  Categorizer, Ranker prompts to emit the contract (Editor is refined in **P3**, leave it).
- **New** `src/NewsAggregator.Tests/Application/EnrichedItemAssemblerTests.cs`.

**Implementation.**
1. **Contract.** Categorizer must return strict JSON `{"category":"<one of taxonomy>","tags":["…"]}`;
   Ranker must return strict JSON `{"score":0.0-1.0,"reason":"<one line>"}`; Summarizer returns plain
   text (2–3 neutral sentences). Define a small fixed taxonomy constant (e.g. `AI`, `Security`,
   `Cloud`, `Devtools`, `Web`, `Data`, `Hardware`, `Other`) per docs §1.4/§3.2 — put the taxonomy in
   Core so both the assembler and (later) the UI filter share it.
2. **Assembler.** Parse defensively (tolerate markdown code fences / surrounding prose by extracting
   the first `{…}` block; use `System.Text.Json`). Then **guarantee `EnrichedItem`'s invariants** so
   construction never throws (the domain requires non-blank `Summary` and `Category`):
   - `Summary` blank → fall back to a trimmed snippet of `NewsItem.Content`, else `NewsItem.Title`.
   - `Category` blank/not-in-taxonomy → `"Other"`. `Tags` → drop null/blank, dedupe, cap at 5.
   - `score` missing/unparseable/out-of-range → `RelevanceScore.Zero`; otherwise
     `new RelevanceScore(score, reason)`.
   The assembler must be **total** (never throw on bad LLM output) — this is what prevents a
   runtime bug/fix loop later.
3. **Prompts.** Update the three instructions to demand the exact JSON (Categorizer/Ranker) and
   list the taxonomy. Keep them concise.

**Constraints.** Core stays BCL-only (`System.Text.Json` ships in the BCL — allowed). Do not change
`EnrichedItem`/`RelevanceScore`. Do not touch workflows or DI in this prompt.

**Definition of Done.**
- `EnrichedItemAssemblerTests` covers: clean JSON; fenced JSON; missing/partial fields;
  out-of-range/NaN score; non-taxonomy category → `Other`; blank summary → title fallback; tag
  cleanup/cap. All assert a **valid** `EnrichedItem` is produced (no throw).
- `DependencyRuleTests` still green (Core gained no framework reference).
- Full suite green; 0 warn / 0 err.

---

## P2 — Concurrent enrichment workflow

**Goal.** Implement `ConcurrentEnrichmentWorkflow.EnrichAsync` as the fan-out/fan-in enrichment of
docs §3.3: per article, run Summarizer ∥ Categorizer ∥ Ranker, merge via the P1 assembler, stream
progress, and bound cross-article parallelism by `EnrichmentOptions.MaxDegreeOfParallelism`.

**Prerequisites.** **P1** (the `EnrichedItemAssembler` + refined prompts must exist).

**Files in scope.**
- **Modify** `src/NewsAggregator.Infrastructure/Workflows/ConcurrentEnrichmentWorkflow.cs` (replace
  the `NotImplementedException`).
- **New** `src/NewsAggregator.Tests/Fakes/FakeAgentFactory.cs` — an `IAgentFactory` returning agents
  backed by `FakeChatClient` with role-specific canned replies (reuse the existing `FakeChatClient`).
- **New** `src/NewsAggregator.Tests/Workflows/ConcurrentEnrichmentWorkflowTests.cs`.

**Implementation (verify ⚠️ APIs against the installed `Microsoft.Agents.AI.Workflows` — 1.9.0 per `Directory.Packages.props` — first).**
1. For each `NewsItem`, build the agent set from `_agentFactory.CreateAgent(AgentRole.Summarizer
   /Categorizer/Ranker)`.
2. ⚠️ `Workflow wf = AgentWorkflowBuilder.BuildConcurrent(agents, aggregator)` where `aggregator` is
   `Func<IList<List<ChatMessage>>, List<ChatMessage>>` (docs §3.3). In the aggregator, pull each
   agent's last message text and **call the P1 `EnrichedItemAssembler`** — the aggregator does no
   parsing itself. Carry the resulting `EnrichedItem` out (e.g. via a captured local or by emitting
   a serialized result message you map after the run; pick whichever the verified API supports and
   keep parsing in Core).
3. ⚠️ Run with `InProcessExecution.RunStreamingAsync(wf, input)` and consume
   `run.WatchStreamAsync()`; on `AgentResponseUpdateEvent` report an `AgentProgress`
   (`Role`, `Stage="enriching"`, `ProcessedCount`/`TotalCount`) via the `IProgress<AgentProgress>`;
   take the final `EnrichedItem` from the `WorkflowOutputEvent`.
4. **Bound cross-article concurrency** with `SemaphoreSlim(Math.Max(1, MaxDegreeOfParallelism))` +
   `Task.WhenAll` over index-ordered tasks (deterministic output order) — exactly the pattern the
   source connectors already use. Do **not** rely on `BuildConcurrent` to parallelize across
   articles (docs §3.3 "Batching note").
5. Honour `cancellationToken`; rethrow genuine caller cancellation (fail-fast, matching Core).
   An agent producing junk must still yield a valid item via the P1 assembler (never throw).

**Constraints.** All Agent Framework types stay in Infrastructure. Progress reporting must be
null-safe (`progress?.Report`). Output count/order: one `EnrichedItem` per input item, input order
preserved.

**Definition of Done.**
- Tests (with `FakeAgentFactory`/`FakeChatClient`, no live model): N items → N enriched items in
  order; assembler integration (canned categorizer/ranker JSON → expected `Category`/`Relevance`);
  progress is reported; `MaxDegreeOfParallelism` is respected (assert observed concurrency ≤ limit);
  cancellation throws. Deterministic.
- Full suite green; 0 warn / 0 err.

---

## P3 — Sequential editorial workflow

**Goal.** Implement `SequentialEditorialWorkflow.ComposeAsync` as the editorial pipeline of docs
§3.4: deterministic **sort by score desc** → **group by category** → **Editor agent** writes a short
intro per section → assemble the final `Digest`.

**Prerequisites.** **P1** (taxonomy/`EnrichedItem` conventions). Independent of P2.

**Files in scope.**
- **New** `src/NewsAggregator.Core/Application/Editorial/DigestComposer.cs` — pure Core helper:
  `IReadOnlyList<EnrichedItem>` → ordered `DigestSection`s (sort by `Relevance.Value` desc with a
  stable tie-break, e.g. `Item.Title` ordinal; group by `Category`; order sections by their top
  item's score). `Intro` left null here (the Editor fills it). BCL-only, fully testable.
- **Modify** `src/NewsAggregator.Infrastructure/Workflows/SequentialEditorialWorkflow.cs` (replace
  the `NotImplementedException`): call `DigestComposer` for deterministic structure, then run the
  Editor agent to produce per-section intros, set `Digest.GeneratedAt = DateTimeOffset.UtcNow`
  (or an injected clock — see note), and return the `Digest`.
- **Modify** `src/NewsAggregator.Infrastructure/Agents/AgentInstructions.cs` — refine the **Editor**
  prompt to return section intros in a parseable shape (e.g. JSON keyed by category) so mapping back
  is deterministic; reuse the defensive-parse approach from P1 (a tiny intro-parser may live in Core
  next to `DigestComposer`).
- **New** `src/NewsAggregator.Tests/Application/DigestComposerTests.cs`.
- **New** `src/NewsAggregator.Tests/Workflows/SequentialEditorialWorkflowTests.cs`
  (reuse `FakeAgentFactory` from P2 if present; otherwise add a minimal local fake — keep it
  self-contained so this prompt is buildable even if P2 is not yet merged).

**Implementation (verify ⚠️ APIs against the installed `Microsoft.Agents.AI.Workflows` — 1.9.0 per `Directory.Packages.props` — first).**
1. `DigestComposer` produces deterministic, intro-less sections (pure; no LLM).
2. ⚠️ Editorial pipeline: docs §3.4 says use `AgentWorkflowBuilder.BuildSequential(agents)` when
   all-agents, or `WorkflowBuilder` + `AddEdge(...)` when mixing deterministic executors with the
   agent. **Simplest robust MVP path:** do the deterministic sort/group in `DigestComposer` (Core),
   then invoke **only the Editor agent** for intros via the agent's run API (or a one-agent
   `BuildSequential`) executed through `InProcessExecution.RunStreamingAsync`, streaming
   `AgentResponseUpdateEvent` → `AgentProgress(Stage="composing")`. Avoid wiring custom executors
   unless you have verified the `WorkflowBuilder.AddEdge` surface — prefer the Core helper to keep
   deterministic logic out of the framework (docs §3.4 rationale).
3. Map the Editor's parsed intros back onto the sections (by category); unknown/missing intro →
   leave `Intro = null` (it's optional on `DigestSection`).
4. **Clock note:** `Digest.GeneratedAt` must be non-default. Using `DateTimeOffset.UtcNow` is
   acceptable for MVP, but it makes the workflow's output time-dependent. If you want
   deterministic tests, inject a `TimeProvider` (BCL) into the workflow via DI **and** register it
   in the composition root — that is an additive change. **Ask the user** which they prefer before
   adding `TimeProvider` (constraint C); default to `DateTimeOffset.UtcNow` + asserting
   `GeneratedAt != default` if no answer.

**Constraints.** Deterministic ordering/grouping is **pure Core** and must be tested without any
agent. Agent Framework types stay in Infrastructure. Null-safe progress.

**Definition of Done.**
- `DigestComposerTests`: sort desc + stable tie-break; grouping by category; section ordering;
  empty input → empty `Digest` (still valid, `GeneratedAt` set by the workflow not the composer);
  items with equal scores deterministic.
- `SequentialEditorialWorkflowTests` (fake Editor): intros mapped to correct sections; missing intro
  → null; `GeneratedAt` non-default; progress reported; cancellation throws.
- Full suite green; 0 warn / 0 err.

---

## P4 — Blazor UI: live progress, real refresh, category/tag filtering

**Goal.** Make `Digest.razor` (a) drive a real refresh end-to-end, (b) stream live agent progress to
the browser, and (c) filter the rendered digest by category/tag — delivering user stories §1.3 and
the "watch agents process items live" story of §1.5/§2.4. Remove the scaffold
`catch (NotImplementedException)`.

**Prerequisites.** **P2 + P3** (so `RefreshDigestAsync` returns a real `Digest`).

**Files in scope.**
- **Modify** `src/NewsAggregator.Web/Components/Pages/Digest.razor`.
- **New** `src/NewsAggregator.Web/.../DigestFilter.cs` *(or in Core if you prefer reuse)* — a **pure**
  filter function `(Digest, category?, tag?) → filtered view` so the logic is unit-tested, not buried
  in markup (constraint: no logic in UI).
- **New** `src/NewsAggregator.Tests/.../DigestFilterTests.cs`.

**Implementation.**
1. **Live progress.** Create an `IProgress<AgentProgress>` (`new Progress<AgentProgress>(p => …)`)
   that updates component state and calls `InvokeAsync(StateHasChanged)`; pass it to
   `DigestService.RefreshDigestAsync(progress, ct)`. Render the current `Stage` /
   `ProcessedCount`/`TotalCount`. (Blazor Server already streams DOM diffs over SignalR — no custom
   hub needed; docs §2.4/§7.2-C.)
2. **Real refresh.** Remove the `NotImplementedException` catch. Keep a generic `try/catch` that
   surfaces a friendly error string and logs (inject `ILogger`), so a model/provider failure shows a
   message instead of a blank page.
3. **Filtering.** Add a category dropdown (from the Core taxonomy constant defined in P1) and/or a
   tag selector; apply the pure `DigestFilter` to produce the rendered view. Show full digest when no
   filter is selected.
4. Render summary, category, tags, and relevance score per item (the page currently shows only the
   title link) so the digest is actually "categorized, ranked, summarized" per §1.7.

**Constraints.** No business logic in the component beyond view wiring; all filtering/transform in
the pure helper. Do not introduce a new SignalR hub (Blazor Server handles streaming). Do not add
bUnit unless the user asks — test the pure `DigestFilter`, verify the page by building + running.

**Definition of Done.**
- `DigestFilterTests`: filter by category; by tag; no-filter passthrough; empty result handled.
- `Web` builds 0 warn / 0 err; full suite green.
- Manual/`/run`: `Refresh digest` shows live stage updates then a filterable, categorized digest
  (with a configured provider). Record the run in the session log.

---

## P5 — Model-provider health check & startup validation

**Goal.** Add a health check that pings the **active** model provider (Ollama endpoint reachability /
OpenRouter reachability) so the Aspire dashboard and `/health` show provider status immediately
(docs §5.3 "a startup health check pings the active model provider", §6.2).

**Prerequisites.** None (providers + config already exist). Fully independent.

**Files in scope.**
- **New** `src/NewsAggregator.Infrastructure/HealthChecks/ModelProviderHealthCheck.cs` —
  implements `Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck`; reads `ModelOptions`,
  performs a cheap reachability probe (e.g. `GET {ollamaEndpoint}` or `/api/tags` for Ollama; a
  lightweight reachability check for OpenRouter that **never logs the key**). Use
  `IHttpClientFactory`; bounded timeout; map success/failure to `HealthCheckResult`.
- **Modify** `src/NewsAggregator.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
  (or `Web/Program.cs`) — register the health check via `AddHealthChecks().AddCheck<…>("model-provider")`.
  Confirm it is surfaced by the existing `MapDefaultEndpoints()` (ServiceDefaults health route).
- **New** `src/NewsAggregator.Tests/HealthChecks/ModelProviderHealthCheckTests.cs` (use
  `FakeHttpMessageHandler`).

**Implementation.** Select the probe by `ModelOptions.Provider`. Ollama: a 200 from the endpoint's
tags/root = Healthy; connection failure/timeout = Unhealthy with a non-secret description.
OpenRouter: reachability of the base URL = Healthy (do not require a real completion; never include
the key in the result). Keep the probe fast and side-effect-free.

**Constraints.** Never log or surface the API key. Probe must not block startup (health checks run
out-of-band). No Core change.

**Definition of Done.**
- Tests: Ollama healthy (200) / unhealthy (timeout or non-200) via fake handler; OpenRouter
  reachable/unreachable; key never appears in any `HealthCheckResult` text.
- Build 0 warn / 0 err; full suite green.

---

## P6 — AppHost Ollama model bootstrap + end-to-end smoke test

**Goal.** Make a single `dotnet run` on the AppHost actually able to produce a digest by ensuring the
configured Ollama model is present, and add the DoD's **one workflow smoke test** exercising
collect → enrich → compose end-to-end through the real workflows with fakes.

**Prerequisites.** **P2 + P3** (real workflows). Benefits from **P5** (provider health).

**Files in scope.**
- **Modify** `src/NewsAggregator.AppHost/AppHost.cs` — after the `ollama` container is up, ensure the
  default model is pulled so the app can serve requests on first run.
- **New** `src/NewsAggregator.Tests/Workflows/DigestPipelineSmokeTests.cs` — the end-to-end smoke test.
- (Possibly **New**) a tiny `appsettings`/AppHost wiring note documenting the model name source.

**Implementation.**
1. **Model bootstrap (verify ⚠️ the installed Aspire API first — 13.4.2 per `Directory.Packages.props`/`global.json`).** The Ollama runtime is a first-party
   generic container (`AddContainer("ollama", "ollama/ollama")`) — Aspire has **no** first-party
   Ollama model-pull integration (docs §6.1). Choose **one** verified approach and confirm it with
   `microsoft_docs_search`/the Aspire API before coding:
   - (a) a container/resource lifecycle hook or one-shot command that runs `ollama pull <model>`
     against the container after it is healthy; or
   - (b) keep the AppHost as-is and add a **documented operator step** + a startup readiness gate
     (the P5 health check already reports provider readiness), i.e. the digest refresh degrades to a
     clear "model not available" message (already handled by P4's error path) until the operator runs
     `ollama pull`.
   The volume already persists the model across runs. **Ask the user** to choose (a) vs (b) if the
   verified Aspire surface makes (a) non-trivial (constraint C) — do not invent an API.
2. **Smoke test.** Wire `DigestApplicationService` over: a fake/real `NewsAggregationService` with
   `FakeNewsSource`(s), the **real** `ConcurrentEnrichmentWorkflow` + `SequentialEditorialWorkflow`
   driven by `FakeAgentFactory`/`FakeChatClient` (canned summary + valid categorizer/ranker/editor
   JSON), and `InMemoryDigestCache`. Assert: a non-empty, categorized, score-ordered `Digest` comes
   out; the cache is written once; progress stages fire in order (`collecting → enriching →
   composing → done`). Fully offline + deterministic.

**Constraints.** AppHost is infrastructure composition only — no business logic. Build the AppHost
with the **real SDK on PATH** (needs `Aspire.AppHost.Sdk`). The smoke test must not call any live
model or network.

**Definition of Done.**
- `DigestPipelineSmokeTests` green: end-to-end digest produced through the real workflows with fakes.
- AppHost builds 0 warn / 0 err; the chosen bootstrap approach is documented in the session log and
  reflected in `docs` only if it changes operator instructions (otherwise leave docs untouched).
- Full suite green. After this prompt, **all of `docs/07 §7.5` is satisfied** → MVP complete.

---

## Appendix — verification commands (per session notes)

```bash
# SDK is pinned to 10.0.300 by global.json. If absent, install it (prior sessions used):
#   curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 10.0.300 --install-dir /tmp/dotnet
export PATH=/tmp/dotnet:$PATH      # real SDK on PATH (required for AppHost)

cd News-Aggregator/src
dotnet build NewsAggregator.Infrastructure/NewsAggregator.Infrastructure.csproj   # expect 0/0
dotnet build NewsAggregator.Web/NewsAggregator.Web.csproj                         # expect 0/0
dotnet build NewsAggregator.AppHost/NewsAggregator.AppHost.csproj                 # P6 only; needs real SDK
dotnet test  NewsAggregator.Tests/NewsAggregator.Tests.csproj                     # full suite must pass
```

Baseline at time of writing: **119 passed, 0 failed** (`.claude/plans/05-github-source.md`). Each
prompt must keep that number monotonically non-decreasing (new tests added, none broken).
</content>
