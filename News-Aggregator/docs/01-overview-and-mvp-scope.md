# 1. Overview & MVP Scope

[← Index](README.md) · [Next: Architecture & Project Structure →](02-architecture-and-project-structure.md)

## 1.1 Problem statement

Keeping up with technology news means checking many disconnected sources (aggregators,
news sites, blogs), each with its own format, signal-to-noise ratio, and editorial
slant. Readers want **one curated, de-duplicated, summarized digest** with consistent
categorization and a relevance signal — without hand-rolling prompt pipelines.

The aggregator solves this by:

1. **Ingesting** items from multiple heterogeneous sources behind a single contract.
2. **Enriching** each item with a small team of LLM agents (summary, category/tags,
   relevance score), run **concurrently** for speed.
3. **Composing** a final, ordered digest with a **sequential** editorial pipeline.
4. **Serving** the digest in a Blazor Server UI with live progress as agents work.

## 1.2 Product summary

| Aspect | MVP decision |
| --- | --- |
| Platform | .NET 10 |
| UI | Blazor Server (server-rendered, streams agent events over SignalR) |
| Sources | **Hacker News** (Firebase API, keyless) + **RSS/Atom** (config-driven) |
| Agent runtime | Microsoft Agent Framework (`Microsoft.Agents.AI*`) ✅ |
| Model abstraction | `Microsoft.Extensions.AI` (`IChatClient`) ✅ |
| Local models | Ollama via `OllamaSharp` ✅ |
| Hosted models (BYOK) | OpenRouter via OpenAI-compatible SDK ✅ |
| Local composition | .NET Aspire (AppHost) ✅ |
| Persistence | In-memory + optional distributed cache (Redis) only — **no database in MVP** |

## 1.3 Primary user stories (MVP)

- *As a reader*, I trigger a "Refresh digest" and watch agents process items live,
  then see a categorized, ranked, summarized list.
- *As a reader*, I filter the digest by category/tag.
- *As an operator*, I choose the model provider (local Ollama vs OpenRouter) and model
  via configuration, with **no code change**.
- *As an operator*, I add a new RSS feed by editing configuration — no redeploy of logic.

## 1.4 Agent team (MVP)

| Agent | Responsibility | Notes |
| --- | --- | --- |
| **Summarizer** | 2–3 sentence neutral summary of an article. | Runs concurrently per item. |
| **Categorizer** | Assign a category + tags (AI, Security, Cloud, Devtools, …). | Concurrent; structured output. |
| **Relevance Ranker** | Score 0–1 for "tech significance". | Concurrent; numeric output. |
| **Editor** | Compose the final ordered digest / section intros. | Sequential, after enrichment. |

> The mapping of these agents onto **Concurrent** (enrichment fan-out/fan-in) and
> **Sequential** (editorial pipeline) workflows is detailed in
> [§3 Agent Orchestration](03-agent-orchestration-design.md).

## 1.5 In scope (MVP)

- Two source connectors (Hacker News, RSS/Atom) behind one `INewsSource` port.
- De-duplication by canonical URL/title hash.
- Concurrent enrichment workflow + sequential editorial workflow.
- Runtime-selectable model provider (Ollama / OpenRouter) and per-agent model.
- Blazor Server UI: trigger refresh, live progress, categorized digest, filtering.
- Aspire AppHost wiring Web + Ollama (+ optional Redis cache) with the dashboard,
  OpenTelemetry, and health checks.
- Unit tests for Core logic and Infrastructure adapters (with fakes), plus a
  workflow smoke test.

## 1.6 Explicitly out of scope (MVP)

- Authentication / multi-user accounts / personalization.
- Durable / persisted workflows and a relational database for articles.
- Additional orchestration patterns (Handoff, Group Chat, Magentic).
- Human-in-the-loop approval (the framework supports it; deferred — see roadmap).
- Embeddings / vector search / semantic dedupe (keyword/URL dedupe only).
- Non-English content, sentiment analysis, push notifications, mobile clients.
- Production hardening (rate-limit backoff policies, secrets vault, CDN, scaling).

## 1.7 Success criteria

- A single "Refresh" produces a categorized, ranked, summarized digest from both
  sources in one run.
- Switching `Ollama` ↔ `OpenRouter` is a configuration-only change.
- Adding an RSS feed is a configuration-only change.
- `Core` references **no** framework/provider package (enforced by project refs).
- The whole system starts with a single `dotnet run` on the AppHost.

## 1.8 Quality attributes that drive the design

| Attribute | How the design addresses it |
| --- | --- |
| **Maintainability** | Clean layering, DI, small single-purpose agents/services. |
| **Portability of models** | `IChatClient` abstraction; providers are swappable adapters. |
| **Observability** | Aspire dashboard + OpenTelemetry via `.UseOpenTelemetry()` on the chat pipeline and ServiceDefaults. |
| **Responsiveness** | Concurrent enrichment (fan-out/fan-in) reduces latency; live event streaming to UI. |
| **Cost / privacy control** | Local Ollama by default; OpenRouter opt-in per environment. |

[← Index](README.md) · [Next: Architecture & Project Structure →](02-architecture-and-project-structure.md)
