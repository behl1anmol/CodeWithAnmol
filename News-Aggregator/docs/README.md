# Multi-Source Tech News Aggregator — Architecture Documentation

> **Status:** Design / architecture only. This repository folder contains an
> architecture specification for an MVP. **No application code is included** —
> the purpose is to lock down structure, package choices, and orchestration
> design *before* writing any code.

A **.NET 10** application that aggregates technology news from multiple sources
(MVP: **Hacker News** + **RSS/Atom feeds**), enriches each item with a team of
LLM-backed agents (summarize, categorize, rank, compose), and presents a curated
digest in a **Blazor Server** UI. Agent coordination uses the **Microsoft Agent
Framework** (the successor to Semantic Kernel and AutoGen) with both **Concurrent**
and **Sequential** workflows. Models are served by **Ollama** (local LLM) and
**OpenRouter** (bring-your-own-key), abstracted behind **Microsoft.Extensions.AI**.
Everything is composed and run locally with **.NET Aspire**.

---

## ⚠️ Accuracy policy (no hallucinated APIs)

A core constraint of this design is that **every Microsoft Agent Framework / Aspire
package, type, and method referenced has been verified against Microsoft Learn or
nuget.org**. Anything that could not be fully verified is explicitly marked and
accompanied by an alternative. Use the legend below throughout the docs:

| Marker | Meaning |
| --- | --- |
| ✅ | Verified against official Microsoft Learn docs or nuget.org. |
| ⚠️ | Real, but **version / exact option must be re-verified** at implementation time (preview→GA churn). |
| ❓ | **Uncertain** — an alternative is provided; confirm before coding. |

> **Versioning caveat (read first):** Microsoft Agent Framework reached 1.0 in
> 2026. At the time of writing, the `Microsoft.Agents.AI` core package reports a
> `1.x` GA line, while some companion API-reference pages (Workflows, OpenAI bridge)
> still display `1.0.0-rc2`. **Do not hard-code a version from these docs** — pin
> versions from nuget.org during implementation and keep all
> `Microsoft.Agents.AI.*` packages on the **same** version.

---

## Table of contents

1. [Overview & MVP Scope](01-overview-and-mvp-scope.md)
2. [Architecture & Project Structure](02-architecture-and-project-structure.md)
3. [Agent Orchestration Design (Concurrent + Sequential)](03-agent-orchestration-design.md)
4. [Model Providers & BYOK (Ollama + OpenRouter)](04-model-providers-and-byok.md)
5. [Packages & Configuration Strategy](05-packages-and-configuration.md)
6. [Aspire Topology & Docker Strategy](06-aspire-topology-and-docker.md)
7. [SOLID, Tradeoffs & Roadmap](07-solid-tradeoffs-and-roadmap.md)

---

## Design principles (non-negotiable)

- **Pragmatic Clean Architecture** — exactly five projects: `AppHost`, `Web`,
  `Core`, `Infrastructure`, `Tests`. No speculative extra projects.
- **Dependency Injection everywhere** — no static service locators, no
  `ServiceLocator`/`Activator` shortcuts.
- **No God classes** — single-responsibility services and agents.
- **No business logic in the UI** — Blazor components call application services only.
- **No framework coupling in the Domain** — `Core` depends on nothing but BCL and
  its own abstractions (it does **not** reference Agent Framework, Aspire, ASP.NET,
  or provider SDKs).
- **Local LLM + BYOK from day one** — Ollama and OpenRouter are both first-class,
  selectable at runtime.
- **Maintainability over cleverness.**

## How to publish these docs (later)

The folder is structured to publish as a **GitHub Pages** site: each numbered file
is a standalone page, links are relative, diagrams are Mermaid (rendered natively by
GitHub), and a minimal [`_config.yml`](_config.yml) selects a Jekyll theme. Point
Pages at `/docs` on the default branch when ready.
