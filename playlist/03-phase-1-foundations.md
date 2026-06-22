# 03 — Phase 1: Foundations (Episodes 0–3)

[← Curriculum](02-curriculum-overview.md) · [Index](README.md) · [Next: Phase 2 →](04-phase-2-news-sources.md)

**Phase accent color suggestion:** slate/blue. **Goal of the phase:** stand up the 5-project Clean
Architecture, model the domain, and define the ports — so every later episode has a clean place to
plug a feature in. By the end of Ep 3 the *shape* of the whole app exists; only the behavior is
missing.

---

## Episode 0 — Trailer: "What we're building & why" (~3–5 min)

**Hook.** "Most AI tutorials stop at one prompt and one answer. We're going to build a team of four
AI agents that read tech news and hand you a curated, ranked, summarized digest — and we'll keep it
clean enough that a test suite *refuses* to let us cut corners."

**Learning objectives (what the viewer will know by series end):**
- How to orchestrate multiple LLM agents with the Microsoft Agent Framework.
- How `Microsoft.Extensions.AI` abstracts local (Ollama) and cloud (OpenRouter) models.
- How to keep an AI app in Clean Architecture and run it with one `dotnet run` via Aspire.

**Talk segment.** Show the **finished app** first (the payoff). Then a 60-second tour of the
architecture diagram from `docs/02 §2.2` (Core ← Infrastructure ← Web ← AppHost) and the two
workflows from `docs/03` (concurrent enrichment, sequential editorial). Explain the **format**: each
episode starts from an empty copy, we build one feature, we demo it. Mention the dual-screen/repo
tags so viewers can follow along.

**No build.** This is the promise-and-map video.

**Demo.** The live finished app: hit Refresh, watch agents work, see the categorized digest, switch
a filter. End on "by the end of this series, you'll have built every piece of that."

**Visuals/B-roll.** The `docs/02` and `docs/03` Mermaid diagrams; a fast montage of upcoming
episodes.

**Repo tag.** `end-ep16` (the finished app) for the demo.

**Title/thumbnail.** `Build a Multi-Agent AI App in .NET 10 (Full Course)` · thumbnail: the digest
UI + "4 AI AGENTS."

---

## Episode 1 — Solution & Clean Architecture skeleton (~22–26 min)

**Hook.** "Before we write a single line of AI code, we'll build an architecture that can't rot —
where the rules are enforced by tests, not hope."

**Learning objectives:**
- Create the 5-project solution: `Core`, `Infrastructure`, `Web`, `AppHost`, `Tests`.
- Understand the inward **dependency rule** and set up project references to enforce it.
- Use **Central Package Management** (`Directory.Packages.props`) and shared build settings.
- Write the first guardrail test (`DependencyRuleTests`) that fails if Core gains a forbidden ref.

**Prerequisites.** None. Repo start tag: `start-ep01` (empty skeleton — just folders + plumbing) →
`end-ep01`.

**Talk segment (concepts).** Walk the dependency graph from `docs/02 §2.2`: dependencies point
*inward* toward `Core`; `Core` references nothing external; the composition root (`Web/Program.cs`)
is the only place adapters meet ports. Explain *why* this matters for an AI app specifically: model
SDKs churn fast, so the domain must not depend on them (the whole point of `docs/02 §2.3`).

**Hands-on build (in order):**
1. `NewsAggregator.slnx` + the five project folders.
2. `NewsAggregator.Core` (classlib, **zero packages** — call this out loudly).
3. `NewsAggregator.Infrastructure` → references `Core`.
4. `NewsAggregator.Web` (`Microsoft.NET.Sdk.Web`) → references `Core` + `Infrastructure`.
5. `NewsAggregator.AppHost` (stub for now) and `NewsAggregator.Tests` → references `Core` +
   `Infrastructure`.
6. `Directory.Build.props` (`net10.0`, `Nullable`, `ImplicitUsings`, `LangVersion latest`),
   `Directory.Packages.props` (CPM), `global.json` (SDK `10.0.300`, Aspire SDK pin).
7. `Tests/Architecture/DependencyRuleTests.cs` — assert `Core` has no reference to Agent
   Framework / Extensions.AI / Aspire assemblies.

**Tests to show (selective — yes).** `DependencyRuleTests`. This is the signature moment: *try*
adding a NuGet package to Core on camera, run the test, watch it go red, remove it, green. That one
demo sells the entire architecture philosophy.

**Demo.** `dotnet build` (0/0) and `dotnet test` green. The app does nothing yet — that's expected;
the *structure* is the deliverable.

**Gotchas / verify moments.** Building the **AppHost** needs the real SDK on PATH (it consumes
`Aspire.AppHost.Sdk` from `global.json`) — mention now, fully wired in Ep 15. The source of truth
for versions is `Directory.Packages.props`, not memory.

**Visuals.** The `docs/02` dependency diagram, animated to highlight "arrows point inward."

**Repo tag.** `start-ep01` → `end-ep01`.

**Title/thumbnail.** `Clean Architecture in .NET That a Test Enforces` · "RULES AS TESTS."

---

## Episode 2 — Domain modeling in Core, BCL-only (~24–28 min)

**Hook.** "We'll model our domain so carefully that bad data literally can't exist — even when an
LLM hands us garbage later."

**Learning objectives:**
- Model the core entities: `NewsItem`, `EnrichedItem`, `Digest`/`DigestSection`, `RelevanceScore`,
  `AgentRole`, `ChatModelDescriptor`.
- Encode **invariants** (non-blank title/URL, valid score range, non-blank summary/category) in the
  types themselves.
- Define the `Taxonomy` constant (the fixed category set) once, in Core, so the assembler and the UI
  later share it.

**Prerequisites.** Ep 1. Tag: `start-ep02` → `end-ep02`.

**Talk segment.** Why model invariants in the domain at all? Because the AI layer is
non-deterministic — pushing validation *down* into the types means the rest of the system can trust
its inputs. Preview that in Ep 10 the assembler will *guarantee* these invariants from messy LLM
output. Reference `docs/01 §1.4` for the taxonomy/agent roster.

**Hands-on build (in order):**
1. `Core/Domain/NewsItem.cs` — required non-blank `Title`, absolute `Url`, `Source`, optional
   `Content`/`PublishedAt`.
2. `Core/Domain/RelevanceScore.cs` — value object clamped/validated to `0.0–1.0` + optional reason;
   a `Zero` default.
3. `Core/Domain/EnrichedItem.cs` — wraps `NewsItem` + non-blank `Summary`, `Category`, `Tags`
   (capped), `Relevance`.
4. `Core/Domain/Digest.cs` / `DigestSection` — `GeneratedAt`, ordered sections (category, optional
   intro, items).
5. `Core/Domain/AgentRole.cs` (Summarizer/Categorizer/Ranker/Editor) and
   `ChatModelDescriptor.cs` (provider, model id, endpoint).
6. `Core/Application/Enrichment/Taxonomy.cs` — the fixed list `[AI, Security, Cloud, Devtools, Web,
   Data, Hardware, Other]`.

**Tests to show (selective — yes).** A couple of domain invariant tests: constructing a `NewsItem`
with a blank title throws; `RelevanceScore` rejects out-of-range; `EnrichedItem` requires a
non-blank summary. Show *one* red→green to make the point, then run the rest fast.

**Demo.** Tests green. Optionally a tiny LINQPad/`dotnet run`-less snippet constructing a `Digest`
in memory to make the model tangible.

**Gotchas.** `System.Text.Json` is allowed in Core (it ships in the BCL) — we'll use it in Ep 10
without breaking the "no packages" rule. Keep value objects immutable.

**Visuals.** A simple class diagram of the domain (NewsItem → EnrichedItem → Digest).

**Repo tag.** `start-ep02` → `end-ep02`.

**Title/thumbnail.** `Domain Modeling in C# That Can't Break` · "INVARIANTS."

---

## Episode 3 — Ports & the application service (~24–30 min)

**Hook.** "Now we define the *contracts* of our whole app — the seams where AI, news sources, and
caching will plug in — without referencing a single vendor SDK."

**Learning objectives:**
- Define the Core **ports**: `INewsSource`, `INewsAggregationService`, `IChatModelProvider`,
  `IEnrichmentWorkflow`, `IEditorialWorkflow`, `IDigestCache`, `IDigestApplicationService`.
  (Note: the **agent factory is *not* a Core port** — `IAgentFactory` returns the framework type
  `Microsoft.Agents.AI.AIAgent`, so it lives in **Infrastructure** and arrives in Ep 9. Core sees
  the AI layer only through the two *workflow* ports, `IEnrichmentWorkflow`/`IEditorialWorkflow`.)
- Write `DigestApplicationService` to *orchestrate* collect → enrich → compose → cache, depending
  only on ports.
- Stream progress with `IProgress<AgentProgress>` (a BCL type) — the seam the UI uses later.
- Bind configuration with the Options classes (`SourceOptions`, `ModelOptions`, `EnrichmentOptions`,
  `CacheOptions`).

**Prerequisites.** Ep 2. Tag: `start-ep03` → `end-ep03`.

**Talk segment.** Ports & adapters / dependency inversion in practice (`docs/02 §2.3`). Emphasize
the subtle decision the repo makes: even though `IChatClient` is a `Microsoft.Extensions.AI` type,
Core defines its *own* provider-neutral ports (`IChatModelProvider`) so Core stays BCL-only — the
SDK types live only in Infrastructure (`docs/02` "Where does `IChatClient` live?"). Walk the
happy-path sequence diagram from `docs/02 §2.4`.

**Hands-on build (in order):**
1. `Core/Application/AgentProgress.cs` — `Role`, `Stage`, `ProcessedCount`, `TotalCount`.
2. `Core/Application/Ports/*` — the interfaces above, each with a one-line XML doc explaining its
   responsibility.
3. `Core/Configuration/*Options.cs` — POCO options matching `appsettings.json`.
4. `Core/Application/Services/DigestApplicationService.cs` — orchestrate the use-case against ports;
   report progress stages (`collecting → enriching → composing → done`); write to `IDigestCache`.
5. `DependencyRuleTests` still green (Core gained nothing external).

**Tests to show (selective — yes).** `DigestApplicationServiceOrchestrationTests` with fakes
(`ApplicationFakes`/`FakeNewsSource`): assert it calls collect→enrich→compose in order, reports the
right progress stages, and caches once. This proves the orchestration *without any AI yet* — a great
"design pays off" moment.

**Demo.** The orchestration test suite green; narrate that we've now defined the *entire* app's flow
with zero framework dependencies — the AI is just an adapter we'll slot in.

**Gotchas.** Keep the application service free of any parsing/sorting logic — those are pure Core
helpers built later (`EnrichedItemAssembler` Ep 10, `DigestComposer` Ep 12). Progress reporting must
be null-safe (`progress?.Report(...)`).

**Visuals.** The `docs/02 §2.4` request-flow sequence diagram.

**Repo tag.** `start-ep03` → `end-ep03`.

**Title/thumbnail.** `Ports & Adapters: Design Your App Before the SDKs` · "CONTRACTS FIRST."

---

### Phase 1 wrap (say this on camera at the end of Ep 3)

"We now have the whole *shape* of the app — clean domain, enforced boundaries, the orchestration
flow — and not one line of it depends on an AI SDK, a news API, or a database. Next phase: we make
it real with actual tech-news sources."

[← Curriculum](02-curriculum-overview.md) · [Index](README.md) · [Next: Phase 2 →](04-phase-2-news-sources.md)
