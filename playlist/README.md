# 🎬 YouTube Playlist Plan — Build an **Agentic Tech-News Aggregator**

> **Series working title:** *Build an Agentic Tech-News Aggregator with .NET 10, Aspire & the Microsoft Agent Framework*
>
> A hands-on, build-along playlist that teaches intermediate .NET developers how to design and
> code a real, production-shaped AI application — four LLM agents collaborating over a concurrent
> + sequential workflow to turn raw tech news into a curated, ranked, summarized digest.

This folder is the **complete production plan** for the playlist. It is grounded entirely in the
finished application that lives in [`../News-Aggregator/`](../News-Aggregator) — every episode,
file path, package, and API named here exists in that repo. Nothing is invented.

---

## How to use this plan

1. Read **[`00-strategy-and-audience.md`](00-strategy-and-audience.md)** once — it sets the
   audience, tone, naming/SEO conventions, and the reusable per-episode template.
2. Read **[`01-production-and-recording-setup.md`](01-production-and-recording-setup.md)** before
   you record a single frame — it defines the dual-screen workflow and, crucially, the
   **code-stripping strategy** that gives every episode a clean "empty start" and a known "finished
   end."
3. Use **[`02-curriculum-overview.md`](02-curriculum-overview.md)** as your map: the full episode
   list, the build-order dependency graph, and the episode→repo-file mapping.
4. Work episode-by-episode from the phase docs (`03`…`09`). Each episode is a self-contained
   shooting script: hook, talking points, the exact files to build in order, what to demo, and the
   "gotchas" that make great teaching moments.

---

## The series at a glance

| # | Episode | Phase | Maps to repo |
|---|---------|-------|--------------|
| 0 | Trailer — what we're building & why | Intro | Finished app demo, `docs/02` |
| 1 | Solution & Clean Architecture skeleton | Foundations | 5 projects, `.slnx`, `Directory.Packages.props` |
| 2 | Domain modeling in Core (BCL-only) | Foundations | `Core/Domain/*` |
| 3 | Ports & the application service | Foundations | `Core/Application/Ports/*`, `DigestApplicationService` |
| 4 | Hacker News source | Sources | `HackerNewsSource` |
| 5 | RSS/Atom source | Sources | `RssNewsSource` |
| 6 | GitHub source + aggregation & dedup | Sources | `GitHubNewsSource`, `NewsAggregationService` |
| 7 | Ollama provider & the `IChatClient` pipeline | Providers | `OllamaChatModelProvider`, `ChatClientFactory` |
| 8 | OpenRouter BYOK provider | Providers | `OpenRouterChatModelProvider` |
| 9 | Agent Framework intro + Agent Factory | Agents | `AgentFrameworkAgentFactory`, `AgentInstructions` |
| 10 | Enrichment contract & the total assembler | Agents | `EnrichedItemAssembler` (P1) |
| 11 | **Concurrent enrichment workflow** | Agents | `ConcurrentEnrichmentWorkflow` (P2) |
| 12 | **Sequential editorial workflow** | Agents | `SequentialEditorialWorkflow`, `DigestComposer` (P3) |
| 13 | Blazor Server UI: live progress + filtering | UI | `Digest.razor`, `DigestFilter` (P4) |
| 14 | Health checks & startup validation | Production | `ModelProviderHealthCheck` (P5) |
| 15 | Aspire AppHost + Ollama bootstrap + smoke test | Production | `AppHost.cs`, `DigestPipelineSmokeTests` (P6) |
| 16 | Full demo, recap & roadmap | Finale | End-to-end run, `docs/07` roadmap |

**17 videos** (trailer + 16 builds), each **20–35 minutes**, for roughly **7–9 hours** of
finished content. At one upload per week that's a ~4-month flagship series.

---

## Documents in this folder

| Doc | What's inside |
|-----|---------------|
| [`00-strategy-and-audience.md`](00-strategy-and-audience.md) | Goals, audience, positioning, SEO/naming, thumbnails, cadence, episode template. |
| [`01-production-and-recording-setup.md`](01-production-and-recording-setup.md) | Dual-screen workflow, code-stripping via git tags, tooling, dead-air management, per-video checklist. |
| [`02-curriculum-overview.md`](02-curriculum-overview.md) | Full episode list, dependency graph, episode→file map, ordering rationale. |
| [`03-phase-1-foundations.md`](03-phase-1-foundations.md) | Episodes 0–3. |
| [`04-phase-2-news-sources.md`](04-phase-2-news-sources.md) | Episodes 4–6. |
| [`05-phase-3-model-providers.md`](05-phase-3-model-providers.md) | Episodes 7–8. |
| [`06-phase-4-agents-and-orchestration.md`](06-phase-4-agents-and-orchestration.md) | Episodes 9–12 (the heart of the series). |
| [`07-phase-5-blazor-ui.md`](07-phase-5-blazor-ui.md) | Episode 13. |
| [`08-phase-6-production-and-aspire.md`](08-phase-6-production-and-aspire.md) | Episodes 14–15. |
| [`09-finale-and-channel-growth.md`](09-finale-and-channel-growth.md) | Episode 16 + repurposing/shorts/growth. |

---

## Confirmed parameters

| Decision | Choice |
|----------|--------|
| Audience | Intermediate .NET developers (comfortable with C#/ASP.NET, new to AI agents & Aspire) |
| Episode length | Medium — 20–35 minutes, one coherent feature per video |
| Tests on camera | Show selectively — write/run the highest-value tests live, mention the rest |
| Scope | Full app as built — all 3 sources, both providers, Aspire, health checks |

---

## Grounding & accuracy policy

Everything in this plan traces back to the real repo:

- **Architecture & intent** → `News-Aggregator/docs/01`…`07`
- **Build slicing** → `News-Aggregator/.claude/prompts/mvp-completion-prompts.md` (P1–P6)
- **Verified Agent Framework behavior** → `News-Aggregator/.claude/skills/news-aggregator-dev/references/agent-framework-1.9.md`
- **Package versions** → `News-Aggregator/src/Directory.Packages.props` (the source of truth)

Where the repo flags an API as *"verify against the installed package before using"*, the scripts
in this plan flag it the same way. We teach the **discipline of verifying**, not guessing — that
is itself one of the most valuable lessons of the series.
