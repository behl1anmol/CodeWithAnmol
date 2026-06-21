# 06 — Phase 4: Agents & Orchestration (Episodes 9–12) — the heart of the series

[← Phase 3](05-phase-3-model-providers.md) · [Index](README.md) · [Next: Phase 5 →](07-phase-5-blazor-ui.md)

**Phase accent color suggestion:** orange/amber (the "main event" color). **Goal of the phase:**
turn `IChatClient`s into a team of four specialized **agents** and orchestrate them with the
**Microsoft Agent Framework** — first a **concurrent** fan-out/fan-in enrichment workflow, then a
**sequential** editorial pipeline. This is the material people come for, and the episodes where the
*verify-the-SDK-API* discipline earns its keep.

> **Phase-wide teaching note (state it in Ep 9 and reinforce later).** The Agent Framework's runtime
> behavior is *not* obvious from IntelliSense — the repo's verified reference
> (`.claude/skills/news-aggregator-dev/references/agent-framework-1.9.md`) documents that a workflow
> **silently hangs `Idle` if you don't send a `TurnToken`**. We teach the habit of verifying SDK
> behavior empirically. The single best "save-you-hours" moment in the whole series lives in Ep 11.

---

## Episode 9 — Agent Framework intro + the Agent Factory (~26–32 min)

**Hook.** "A chat client answers questions. An *agent* has a job, a personality, and a contract.
Today we hire four of them."

**Learning objectives:**
- Understand what an `AIAgent` is in the Microsoft Agent Framework, and how `ChatClientAgent` builds
  one over any `IChatClient`.
- Give each agent its *role instructions* (`AgentInstructions`) — the system prompt that defines its
  job and output contract.
- Implement `AgentFrameworkAgentFactory : IAgentFactory` so the rest of the app gets ready-made
  agents and never news-up an SDK type.
- See why `IAgentFactory` returns the framework type `AIAgent` and therefore lives in
  **Infrastructure**, not Core.

**Prerequisites.** Eps 7–8 (an `IChatClient` per role) + Ep 2 (`AgentRole`). Tag: `start-ep09` →
`end-ep09`.

**Talk segment.** The Agent Framework layering (`docs/03 §3.1`): an agent is any `AIAgent`; the
simplest is `new ChatClientAgent(chatClient, instructions)`. Walk the four roles and their **output
contracts** (`docs/01 §1.4`, `docs/03 §3.2`):
- **Summarizer** → 2–3 neutral sentences, *text only*.
- **Categorizer** → strict minified JSON `{"category":"<taxonomy>","tags":[…]}`.
- **Ranker** → strict minified JSON `{"score":0.0-1.0,"reason":"…"}`.
- **Editor** → JSON mapping each category to a short intro (used in Ep 12).
Explain *why* the factory exists: workflows receive ready `AIAgent`s, so the orchestration code is
SDK-construction-free and the agents (which own HTTP pipelines) are created once and reused.

**Hands-on build (in order):**
1. `Infrastructure/Agents/AgentInstructions.cs` — the per-role system prompts (Summarizer,
   Categorizer, Ranker; Editor refined in Ep 12). Keep them concise and contract-explicit.
2. `Infrastructure/Agents/IAgentFactory.cs` (Infrastructure-side port) and
   `AgentFrameworkAgentFactory.cs` — `CreateAgent(AgentRole role)`: describe the model via
   `IChatModelProvider`, build the `IChatClient` via `ChatClientFactory`, return
   `new ChatClientAgent(client, AgentInstructions.For(role))`; cache one agent per role.
3. DI registration for the factory (singleton).

**Tests to show (selective — yes).** With `FakeChatClient`/`FakeAgentFactory`: the factory returns a
distinct agent per role and reuses it. (We don't assert LLM output here — that's the assembler's job
next episode.)

**Demo (payoff).** Ask the **Summarizer** agent to summarize one real article from Ep 6 and print
the result. First time an *agent* (not a raw client) speaks. Optionally show the Categorizer
returning JSON to foreshadow Ep 10.

**Gotchas / verify moments.** `Microsoft.Agents.AI` types (`AIAgent`, `ChatClientAgent`) live **only**
in Infrastructure — because the factory returns `AIAgent`, the port lives in Infrastructure, not
Core (`docs/02 §2.3` nuance). Verify `ChatClientAgent`'s constructor signature against the installed
`Microsoft.Agents.AI 1.9.0`. Introduce the verify-the-API discipline here so Ep 11 lands.

**Visuals.** Four labeled agent "badges" (Summarizer/Categorizer/Ranker/Editor) over one shared
`IChatClient` pipeline.

**Repo tag.** `start-ep09` → `end-ep09`.

**Title/thumbnail.** `Build AI Agents in .NET (Microsoft Agent Framework)` · "4 AGENTS."

---

## Episode 10 — Enrichment contract & the *total* assembler (P1) (~26–32 min)

**Hook.** "LLMs lie, fence their JSON, and go off-script. We'll build a parser that *cannot* fail —
no matter what the model throws at it."

**Learning objectives:**
- Define the parsed-output POCOs (`EnrichmentOutputs`: `CategoryResult`, `RelevanceResult`).
- Build `EnrichedItemAssembler` — a **pure, total** mapper from three raw agent strings (summary,
  categorizer JSON, ranker JSON) to a **valid** `EnrichedItem`, in Core, BCL-only.
- Parse defensively: tolerate code fences/prose, extract the first `{…}` block, use
  `System.Text.Json`.
- Guarantee domain invariants so construction **never throws** (the key reliability property).

**Prerequisites.** Ep 2 (domain + taxonomy), Ep 9 (the agent prompts). Maps to repo **P1**. Tag:
`start-ep10` → `end-ep10`.

**Talk segment.** Why a *separate, pure* assembler instead of parsing inside the workflow
(`docs/03 §3.3`, invariant #5/#6 in the engineering playbook): determinism lives in Core; the LLM
lives in Infrastructure. The assembler is **total** — any junk still yields a valid `EnrichedItem`:
- `Summary` blank → fall back to a trimmed `Content` snippet, else `Title`.
- `Category` blank/not-in-taxonomy → `"Other"`; `Tags` → drop blanks, dedupe, cap at 5.
- `score` missing/NaN/out-of-range → `RelevanceScore.Zero`; else `new RelevanceScore(score, reason)`.
This totality is *what prevents a runtime bug/fix loop later* — say that explicitly.

**Hands-on build (in order):**
1. `Core/Application/Enrichment/EnrichmentOutputs.cs` — `CategoryResult { Category, Tags }`,
   `RelevanceResult { Score, Reason }`.
2. `Core/Application/Enrichment/EnrichedItemAssembler.cs` — defensive JSON extraction + invariant
   guarantees, producing `EnrichedItem`. Pure, no I/O.
3. Refine `AgentInstructions` (Summarizer/Categorizer/Ranker) to emit *exactly* the contract and
   list the taxonomy.
4. `Tests/Application/EnrichedItemAssemblerTests.cs`.

**Tests to show (selective — YES, in depth).** This is a marquee test episode. Show the table of
cases the repo specifies: clean JSON; **fenced** JSON (```` ```json ````); missing/partial fields;
out-of-range/NaN score → `Zero`; non-taxonomy category → `Other`; blank summary → title fallback;
tag cleanup/cap at 5. Every case asserts a **valid** `EnrichedItem` (no throw). Run a deliberately
horrible input live and watch it still produce something valid — memorable.

**Demo.** Feed the assembler the (possibly messy) real outputs from the Ep 9 agents → a clean
`EnrichedItem`. Bridges "agents talk" to "we have structured data."

**Gotchas.** `System.Text.Json` is BCL — allowed in Core; re-run `DependencyRuleTests`. The assembler
must never throw — wrap parses, default everything. Keep parsing **out** of the workflow (Ep 11) so
the framework code stays thin.

**Visuals.** "Garbage in → valid `EnrichedItem` out" funnel with examples of garbage.

**Repo tag.** `start-ep10` → `end-ep10`.

**Title/thumbnail.** `Parse LLM Output That Can't Crash Your App` · "TOTAL PARSER."

---

## Episode 11 — Concurrent enrichment workflow (P2) (~30–35 min) ⭐ flagship

**Hook.** "Three agents, one article, all at once. We'll fan out to the Summarizer, Categorizer, and
Ranker *concurrently* — and I'll show you the one-line gotcha that makes the whole thing hang if you
miss it."

**Learning objectives:**
- Build a **fan-out/fan-in** workflow with `AgentWorkflowBuilder.BuildConcurrent(agents, aggregator)`.
- Drive it: `InProcessExecution.RunStreamingAsync` + `run.WatchStreamAsync()`.
- The **`TurnToken` gotcha** — agents only respond after `run.TrySendMessageAsync(new
  TurnToken(emitEvents: true))`; without it the workflow sits `Idle` forever.
- Stream `AgentResponseUpdateEvent` → `IProgress<AgentProgress>` for live UI later; take the result
  from `WorkflowOutputEvent`; merge via the **Ep 10 assembler** in the aggregator.
- Bound **cross-article** parallelism with `SemaphoreSlim` (don't rely on `BuildConcurrent` to
  parallelize across articles — it parallelizes the agent set over *one* input).

**Prerequisites.** Ep 9 (agents/factory) + Ep 10 (assembler). Maps to repo **P2**. Tag: `start-ep11`
→ `end-ep11`.

**Talk segment.** The fan-out/fan-in (scatter-gather) pattern (`docs/03 §3.3`): all three agents get
the *same* article; the optional `aggregator`
`Func<IList<List<ChatMessage>>, List<ChatMessage>>` reduces their replies. Crucially, the aggregator
does **no parsing** — it pulls each agent's last message text and calls the Ep 10
`EnrichedItemAssembler`. Then the **batching note**: to process many articles, the workflow loops
articles under a `SemaphoreSlim(MaxDegreeOfParallelism)` with `Task.WhenAll` (the same bounded
pattern as the sources), preserving input order.

**Hands-on build (in order):**
1. `Infrastructure/Workflows/ConcurrentEnrichmentWorkflow.cs` — for each `NewsItem`: build the agent
   set from the factory; `AgentWorkflowBuilder.BuildConcurrent([summarizer, categorizer, ranker])`;
   `input = [new ChatMessage(ChatRole.User, BuildPrompt(item))]`;
   `await using var run = await InProcessExecution.RunStreamingAsync(wf, input, ct)`;
   **`await run.TrySendMessageAsync(new TurnToken(emitEvents: true))`**; consume
   `WatchStreamAsync()` — on `AgentResponseUpdateEvent` report progress (route replies by
   `AIAgent.Id`), on `WorkflowOutputEvent` collect the result; merge via the assembler.
2. Wrap the per-article run in the bounded `SemaphoreSlim` + `Task.WhenAll` (index-ordered →
   deterministic output order).
3. `Tests/Fakes/FakeAgentFactory.cs` (agents backed by `FakeChatClient` with role-specific canned
   replies) + `Tests/Workflows/ConcurrentEnrichmentWorkflowTests.cs`.

**Tests to show (selective — YES).** N items → N enriched items **in order**; canned categorizer/
ranker JSON → expected `Category`/`Relevance` (assembler integration); progress is reported;
**`MaxDegreeOfParallelism` is respected** (assert observed concurrency ≤ limit via a counting fake);
cancellation throws. Deterministic, offline.

**Demo (payoff — flagship moment).** *First, on purpose, omit the `TurnToken`* and show the workflow
hang `Idle` — let it sit, then explain why. Add the one line, re-run, and watch three agents enrich
a **small batch** (3–5 real articles) concurrently with live progress in the console. This
failure→fix beat is the most shareable teaching moment of the series.

**Gotchas / verify moments (the whole point of this episode).**
- **`TurnToken` or it hangs** — the headline gotcha, verified in
  `references/agent-framework-1.9.md`.
- Route replies by `AIAgent.Id` (don't assume reply order).
- `BuildConcurrent` parallelizes agents over *one* input, **not** across articles — bound articles
  yourself.
- All Agent Framework types stay in Infrastructure; progress is null-safe; a junk agent reply still
  yields a valid item (Ep 10 assembler). Verify every `Microsoft.Agents.AI.Workflows` API against
  the installed 1.9.0 before relying on it.

**Visuals.** The `docs/03 §3.3` fan-out/fan-in diagram; a side-by-side of "no TurnToken = Idle" vs.
"TurnToken = agents fire."

**Repo tag.** `start-ep11` → `end-ep11`.

**Title/thumbnail.** `Run AI Agents Concurrently in .NET (Fan-Out/Fan-In)` · "CONCURRENT AGENTS" +
a tiny "⚠ TurnToken" tease.

---

## Episode 12 — Sequential editorial workflow (P3) (~28–34 min)

**Hook.** "Now we put on the editor's hat: rank the stories, group them, and let an Editor agent
write the section intros — a pipeline where each step depends on the last."

**Learning objectives:**
- Understand **sequential** orchestration vs. concurrent — and when each is correct
  (`docs/03 §3.6`).
- Build `DigestComposer` (pure Core): sort by relevance desc with a stable tie-break, group by
  category, order sections by their top item's score — *deterministic, no LLM*.
- Run the **Editor** agent (one-agent `BuildSequential`) to produce per-section intros; parse them
  with `EditorIntroParser` and map back by category.
- Assemble the final `Digest` (set `GeneratedAt`).

**Prerequisites.** Ep 10 (taxonomy/`EnrichedItem`). Maps to repo **P3** (independent of P2, but we
teach it after Ep 11 so viewers already have enriched items to compose). Tag: `start-ep12` →
`end-ep12`.

**Talk segment.** Why split deterministic structure from the LLM (`docs/03 §3.4`): sorting/grouping
are pure, testable Core logic; only the *prose* (intros) needs a model. Contrast with Ep 11: here
steps are **dependent** (ordering needs scores; intros need groupings), so sequential is correct —
forcing it concurrent would produce inconsistent ordering (`docs/03 §3.6` anti-pattern). Note the
**clock decision**: `Digest.GeneratedAt` must be non-default; the repo injects a `TimeProvider`
(BCL) for deterministic tests — explain that choice (and that the fakes include `FixedTimeProvider`).

**Hands-on build (in order):**
1. `Core/Application/Editorial/DigestComposer.cs` — `IReadOnlyList<EnrichedItem>` → ordered,
   intro-less `DigestSection`s (sort desc, stable `Title` tie-break, group by category, order
   sections by top score). Pure.
2. `Core/Application/Editorial/EditorIntroParser.cs` — defensive parse of the Editor's JSON
   (`category → intro`), same totality discipline as Ep 10.
3. Refine the **Editor** prompt in `AgentInstructions` to emit that JSON.
4. `Infrastructure/Workflows/SequentialEditorialWorkflow.cs` — compose deterministic sections via
   `DigestComposer`, run the Editor via `AgentWorkflowBuilder.BuildSequential([editor])` +
   `RunStreamingAsync` (stream `AgentResponseUpdateEvent` → `AgentProgress(Stage="composing")`),
   map intros back, set `GeneratedAt` (injected `TimeProvider`), return the `Digest`. Skip the agent
   entirely if there are no items.
5. `Tests/Application/DigestComposerTests.cs` + `Tests/Workflows/SequentialEditorialWorkflowTests.cs`.

**Tests to show (selective — YES).** `DigestComposerTests` (pure, fast): sort desc + stable
tie-break; grouping; section ordering; **empty input → empty digest**; equal scores deterministic.
Then one workflow test (fake Editor): intros mapped to the right sections; missing intro → null;
`GeneratedAt` non-default; progress reported; cancellation throws.

**Demo (payoff).** Feed the enriched items from Ep 11 through the editorial workflow and inspect the
composed `Digest`: ranked items, grouped into category sections, each with an Editor-written intro.
The data side of the product is now *complete* — next episode it goes on screen.

**Gotchas / verify moments.** Keep sort/group **out** of the framework (pure Core) — only intros go
through the agent. Verify `BuildSequential`/`RunStreamingAsync` against installed 1.9.0. The
`TurnToken` lesson from Ep 11 applies to the sequential run too. Missing/unknown intro → leave
`Intro = null` (it's optional).

**Visuals.** The `docs/03 §3.4` sequential pipeline diagram (sort → group → Editor → Digest).

**Repo tag.** `start-ep12` → `end-ep12`.

**Title/thumbnail.** `Sequential AI Agent Pipelines in .NET (Editorial Workflow)` · "RANK · GROUP ·
WRITE."

---

### Phase 4 wrap (say this on camera at the end of Ep 12)

"We've built the brain of the app: four agents, a concurrent enrichment workflow, and a sequential
editorial pipeline that produces a finished, ranked, sectioned digest — all behind clean Core ports,
all tested without ever calling a live model. There's just one thing missing: a human can't *see* it
yet. Next phase, we put it in the browser — live."

[← Phase 3](05-phase-3-model-providers.md) · [Index](README.md) · [Next: Phase 5 →](07-phase-5-blazor-ui.md)
