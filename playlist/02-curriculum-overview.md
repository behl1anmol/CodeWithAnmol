# 02 — Curriculum Overview

[← Production Setup](01-production-and-recording-setup.md) · [Index](README.md) · [Next: Phase 1 →](03-phase-1-foundations.md)

The complete episode list, the build-order dependency graph, the episode→repo-file map, and the
rationale for *why this order*. Use this as the master index when prepping.

---

## 1. The build order (and why it differs from the docs order)

The `docs/01`…`07` chapters are ordered for *reading*. The series is ordered for *building*: each
episode can only use what an earlier episode produced. We start at the dependency graph's root
(Core, which depends on nothing) and move outward to Infrastructure, then Web, then AppHost —
exactly the inward-pointing dependency rule the architecture enforces (`docs/02 §2.2`).

The repo's own work-slicing (`.claude/prompts/mvp-completion-prompts.md`, P1–P6) proves a
buildable order for the *AI* half of the app. We extend it **backwards** with the foundational
episodes (solution, domain, sources, providers) that P1–P6 assume already exist, and **forwards**
into the finale.

```
Ep1 Solution skeleton
  └─ Ep2 Core domain ──┬─ Ep3 Ports + app service
                       │
   ┌───────────────────┴─────────────────────────────┐
   ▼ (Infrastructure adapters, each implements a Core port)
  Ep4 HN source ─ Ep5 RSS source ─ Ep6 GitHub + aggregation/dedup
  Ep7 Ollama provider ─ Ep8 OpenRouter provider
  Ep9 Agent factory
       └─ Ep10 Enrichment assembler (P1)
              ├─ Ep11 Concurrent enrichment workflow (P2)
              └─ Ep12 Sequential editorial workflow (P3)
                     └─ Ep13 Blazor UI (P4)
  Ep14 Health check (P5, independent)
  Ep15 Aspire AppHost + smoke test (P6, needs Ep11+Ep12)
  Ep16 Full demo, recap, roadmap
```

> **Rationale for the ordering choices:**
> - **Domain before everything** — `Core` is the dependency root; nothing compiles against types
>   that don't exist yet.
> - **Sources before providers before agents** — you can show *real data* (Ep 6) before adding AI,
>   and a *real model round-trip* (Ep 7) before wiring agents, so each layer demos independently.
> - **Assembler (Ep 10) before the workflows (Ep 11/12)** — this mirrors P1→{P2,P3}: the pure,
>   total parser is the foundation both workflows depend on, and it's fully testable without a model.
> - **UI after both workflows** — `Digest.razor` needs a real `Digest` to render (P4 needs P2+P3).
> - **Health check is independent** — it can slot anywhere; we place it just before Aspire so the
>   dashboard has something meaningful to show.

---

## 2. Episode → repo-file map

This is the coverage matrix used in the verification step: every project, source, provider, agent,
workflow, and the UI maps to exactly one "owning" episode.

| Ep | Owns (primary files built) | Key packages introduced |
|----|----------------------------|--------------------------|
| 0 | — (trailer; demos finished app) | — |
| 1 | `NewsAggregator.slnx`, all 5 `.csproj`, `Directory.Packages.props`, `Directory.Build.props`, `global.json`, `Architecture/DependencyRuleTests.cs` | (CPM, test SDK, xunit) |
| 2 | `Core/Domain/*` (`NewsItem`, `EnrichedItem`, `RelevanceScore`, `Digest`, `AgentRole`, `ChatModelDescriptor`), `Core/Application/Enrichment/Taxonomy.cs` | BCL only |
| 3 | `Core/Application/Ports/*`, `Core/Application/Services/DigestApplicationService.cs`, `Core/Application/AgentProgress.cs`, `Core/Configuration/*Options.cs` | BCL only |
| 4 | `Infrastructure/Sources/HackerNewsSource.cs`, `Tests/Sources/HackerNewsSourceTests.cs`, `Tests/Fakes/FakeHttpMessageHandler.cs` | `Microsoft.Extensions.Http(.Resilience)` |
| 5 | `Infrastructure/Sources/RssNewsSource.cs`, `Tests/Sources/RssNewsSourceTests.cs` | `System.ServiceModel.Syndication` |
| 6 | `Infrastructure/Sources/GitHubNewsSource.cs`, `Core/Application/Services/NewsAggregationService.cs`, related tests | — |
| 7 | `Infrastructure/Models/OllamaChatModelProvider.cs`, `ChatClientFactory.cs`, `IChatClientFactory.cs` | `OllamaSharp`, `Microsoft.Extensions.AI` |
| 8 | `Infrastructure/Models/OpenRouterChatModelProvider.cs` | `OpenAI`, `Microsoft.Extensions.AI.OpenAI` |
| 9 | `Infrastructure/Agents/AgentFrameworkAgentFactory.cs`, `IAgentFactory.cs`, `AgentInstructions.cs` | `Microsoft.Agents.AI` |
| 10 | `Core/Application/Enrichment/EnrichmentOutputs.cs`, `EnrichedItemAssembler.cs`, `Tests/Application/EnrichedItemAssemblerTests.cs` | BCL (`System.Text.Json`) |
| 11 | `Infrastructure/Workflows/ConcurrentEnrichmentWorkflow.cs`, `Tests/Fakes/FakeAgentFactory.cs`, `Tests/Workflows/ConcurrentEnrichmentWorkflowTests.cs` | `Microsoft.Agents.AI.Workflows` |
| 12 | `Core/Application/Editorial/DigestComposer.cs`, `EditorIntroParser.cs`, `Infrastructure/Workflows/SequentialEditorialWorkflow.cs`, related tests | `Microsoft.Agents.AI.Workflows` |
| 13 | `Web/Components/Pages/Digest.razor`, `Core/Application/Editorial/DigestFilter.cs`, `Tests/Application/DigestFilterTests.cs`, `Web/Program.cs` wiring | Blazor Server, SignalR |
| 14 | `Infrastructure/HealthChecks/ModelProviderHealthCheck.cs`, `Tests/HealthChecks/ModelProviderHealthCheckTests.cs` | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| 15 | `AppHost/AppHost.cs`, `Tests/Workflows/DigestPipelineSmokeTests.cs`, `Web/Extensions/ServiceDefaultsExtensions.cs` | `Aspire.Hosting.AppHost`, `CommunityToolkit.Aspire.Hosting.Ollama`, OpenTelemetry |
| 16 | — (finale; demos + roadmap) | — |

> Some supporting files (e.g. `Web/Program.cs`, `InfrastructureServiceCollectionExtensions.cs`) are
> *touched* across several episodes as new adapters get registered in the composition root. The map
> lists the episode that *introduces* each file; DI registration is added incrementally as part of
> each feature's "wire it up" step.

---

## 3. Mapping to the repo's P1–P6 prompts

| Series episode | Repo prompt | Notes |
|---|---|---|
| Ep 10 | **P1** | Enrichment contract & the pure, total `EnrichedItemAssembler`. |
| Ep 11 | **P2** | Concurrent enrichment workflow (needs P1). |
| Ep 12 | **P3** | Sequential editorial workflow (needs P1). |
| Ep 13 | **P4** | Blazor UI: live progress, real refresh, filtering (needs P2+P3). |
| Ep 14 | **P5** | Model-provider health check (independent). |
| Ep 15 | **P6** | AppHost Ollama bootstrap + end-to-end smoke test (needs P2+P3). |
| Eps 1–9 | *(pre-P1 foundation)* | Solution, domain, ports, sources, providers, agent factory — the scaffolding P1–P6 assume exists. |

The repo's per-prompt plans and checkpoints (`.claude/plans/`, `.claude/checkpoints/`) are gold for
prepping Episodes 10–15: they record exactly what changed and the test counts at each boundary.

---

## 4. Running totals & demo milestones

| After episode | The app can… | Best on-camera demo |
|---|---|---|
| Ep 3 | …model a digest in memory; nothing runs end-to-end yet. | Unit tests of domain invariants. |
| Ep 6 | …fetch & merge real tech news from 3 sources, deduped. | Console-print a live, deduped feed. |
| Ep 8 | …call a local **or** cloud LLM through one `IChatClient`. | Same prompt, swap provider by config. |
| Ep 9 | …create the 4 agents from instructions. | Ask one agent (e.g. Summarizer) to summarize a string. |
| Ep 11 | …enrich an article with 3 agents **concurrently**. | Live small-batch enrichment with progress. |
| Ep 12 | …compose a full ranked, grouped `Digest` with intros. | Inspect the composed digest object. |
| Ep 13 | …do all of it in the browser with live progress + filters. | Click **Refresh**, watch agents, filter. |
| Ep 15 | …start the whole stack with **one `dotnet run`**. | Aspire dashboard + the app, end-to-end. |
| Ep 16 | …the finished product. | The grand demo + provider swap. |

[← Production Setup](01-production-and-recording-setup.md) · [Index](README.md) · [Next: Phase 1 →](03-phase-1-foundations.md)
