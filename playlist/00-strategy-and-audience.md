# 00 — Strategy, Audience & Conventions

[← Index](README.md) · [Next: Production & Recording Setup →](01-production-and-recording-setup.md)

This document defines *who* the series is for, *what* makes it worth watching, and the *reusable
conventions* (naming, SEO, thumbnails, cadence, episode template) that keep 17 videos feeling like
one coherent course.

---

## 1. Why this series exists (the value proposition)

Most "build an AI app" tutorials stop at *"call the model and print the answer."* This series goes
where intermediate developers actually struggle: **orchestrating multiple agents, keeping the
design clean, and shipping something that runs.** The app is deliberately production-shaped, so
every episode teaches a transferable skill, not a toy.

The three promises we make to the viewer (and repeat in the trailer):

1. **You will build a real multi-agent system** — four specialized LLM agents (Summarizer,
   Categorizer, Ranker, Editor) collaborating, not one mega-prompt.
2. **You will learn the Microsoft Agent Framework + `Microsoft.Extensions.AI` properly** — the
   abstractions (`IChatClient`, `AIAgent`), the orchestration patterns (concurrent fan-out/fan-in
   and sequential pipeline), and the **real gotchas** the docs don't make obvious.
3. **You will keep it clean and runnable** — Clean Architecture that a test suite *enforces*, dual
   local/cloud model providers swappable by config, and a single `dotnet run` via .NET Aspire.

> **Rationale.** These three promises map directly to the repo's actual differentiators: the
> 4-agent roster (`docs/01 §1.4`), the two verified orchestration patterns (`docs/03`), and the
> Clean-Architecture + Aspire story (`docs/02`, `docs/06`). We are marketing what genuinely exists.

---

## 2. Audience

**Primary:** intermediate .NET developers. They know C#, `async`/`await`, dependency injection,
and ASP.NET basics. They are **new to**: AI agents, the Microsoft Agent Framework,
`Microsoft.Extensions.AI`, and .NET Aspire.

**What that means for pacing:**

- **Don't** re-teach C# syntax, LINQ, or what DI is. A one-line reminder is fine; a lecture is not.
- **Do** slow down for: the `IChatClient` abstraction, what an `AIAgent` is, the concurrent vs.
  sequential workflow distinction, `TurnToken` semantics, and how Aspire wires containers.
- **Do** name the "why" behind every architectural choice — intermediate devs are ready for design
  reasoning and that's what differentiates this from beginner content.

**Secondary reach:** senior devs/architects skimming for the Agent Framework patterns, and
ambitious beginners who'll pause-and-rewind. We serve them with timestamps/chapters and a linked
repo, without slowing the primary audience.

**Assumed environment:** .NET 10 SDK, an IDE (VS / VS Code / Rider), Docker (for Aspire + Ollama),
and either a machine that can run a small local model (Ollama `llama3.2`) or an OpenRouter API key.
State these prerequisites in the trailer and in every video description.

---

## 3. Prerequisites we state up front

In the trailer and pinned in each description:

- .NET 10 SDK (`10.0.300+`), pinned by `global.json`.
- Docker Desktop (Aspire orchestration + the Ollama container).
- ~8 GB free RAM to run `llama3.2` locally, **or** an OpenRouter API key (we show both).
- Comfort with C# and basic ASP.NET. No prior AI/agent experience required.

---

## 4. Naming & SEO conventions

**Playlist title:** `Build an Agentic AI App in .NET 10 (Agent Framework + Aspire)`

**Episode title pattern:** `#NN — <Concrete outcome> | <Series tag>`
Lead with the *outcome and the searchable tech*, not cleverness.

Examples (grounded in real episode content):

- `#02 — Domain Modeling in C# That Can't Break | Agentic .NET`
- `#07 — Run a Local LLM in .NET with Ollama + Microsoft.Extensions.AI`
- `#11 — Concurrent AI Agents: Fan-Out/Fan-In with the Agent Framework`
- `#15 — One 'dotnet run': Orchestrate Your AI App with .NET Aspire`

**High-value keywords to work into titles/descriptions/tags:** *Microsoft Agent Framework,
Microsoft.Extensions.AI, IChatClient, .NET Aspire, Ollama, local LLM, OpenRouter, Blazor Server,
multi-agent, AI orchestration, Clean Architecture .NET, .NET 10.*

> **Rationale.** "Microsoft Agent Framework" and "Microsoft.Extensions.AI" are low-competition,
> high-intent search terms in 2025–26; the app uses both as first-class citizens, so we can rank
> honestly. Each term above corresponds to a package actually pinned in `Directory.Packages.props`.

**Description template (every video):**

```
In this episode we <build X> for our agentic tech-news aggregator.
00:00 Intro & what we're building today
0X:XX <concept segment>
0X:XX <hands-on build>
0X:XX Demo
0X:XX Recap & what's next

🔗 Full source code: <repo link> (branch/tag for this episode in the pinned comment)
🧰 Stack: .NET 10 · Microsoft Agent Framework 1.9 · Microsoft.Extensions.AI 10.6 · .NET Aspire · Ollama / OpenRouter · Blazor Server
▶ Playlist: <playlist link>
```

Chapters (the `00:00`-style timestamps) are mandatory — they improve retention and let the
secondary audience jump to the part they want.

---

## 5. Thumbnails & visual brand

A consistent thumbnail system so the playlist reads as one course:

- **Fixed left band:** episode number (`#11`) big, plus a small fixed series logo/stack badge.
- **Right side:** 3–5 word benefit (`CONCURRENT AGENTS`, `LOCAL LLM`, `LIVE AGENT PROGRESS`).
- **One accent color per phase** (Foundations / Sources / Providers / Agents / UI / Production) so
  viewers feel progression through the playlist grid.
- A small **architecture-node glyph** highlighting which box of the system this episode builds
  (reuse the `docs/02` dependency diagram as a recurring motif).

> **Rationale.** Numbered, phase-colored thumbnails signal "structured course, watch in order,"
> which is exactly the binge behavior a build-along series wants.

---

## 6. Cadence & series arc

- **One episode per week**, released in order. 17 weeks ≈ 4 months.
- Optionally batch-record a phase at a time (the code-stripping tags in
  [`01-production-and-recording-setup.md`](01-production-and-recording-setup.md) make this safe).
- **Mini-arc within each phase:** open the phase with its "why," close it with a working demo, so
  even a 3-video phase feels complete.
- **Three natural "payoff" peaks** to promote hard: Ep 11 (agents run concurrently), Ep 13 (it's
  alive in the browser), Ep 16 (the full demo). These are the shareable moments.

---

## 7. The reusable per-episode template

Every episode in docs `03`–`09` is written against this template. Keep it identical so scripting,
recording, and editing become a repeatable assembly line.

| Section | Purpose | On-camera time (of ~25 min) |
|---|---|---|
| **Cold open / hook** | 15–30s: the outcome they'll have by the end + why it matters. | ~0:30 |
| **Recap & today's goal** | Where we are in the architecture diagram; what this video adds. | ~1:30 |
| **Talk segment (concepts)** | Explain the feature & the key ideas *before* typing. Slides/diagram. | ~4–6 min |
| **Hands-on build** | Code it from the empty start, file-by-file, narrating decisions. | ~12–16 min |
| **Tests (selective)** | Write/run the high-value test(s) for what we just built. | ~2–4 min |
| **Demo** | Run it; show the feature working. | ~2–3 min |
| **Recap & cliffhanger** | What we built, why it's clean, what's next. CTA. | ~1 min |

**Per-episode spec fields** (filled in for each episode in the phase docs):

- **Title & hook**
- **Learning objectives** (3–5 bullets)
- **Prerequisites** (which earlier episodes / repo start tag)
- **Talk segment** (the concepts to explain, grounded in `docs/`)
- **Hands-on build** (ordered list of files to create, with real repo paths)
- **Tests to show** (which, and why those)
- **End-of-video demo** (what runs, expected result)
- **Gotchas / "verify the API" moments** (the teaching gold)
- **Visuals / B-roll** (diagrams, dashboard, terminal)
- **Estimated runtime**
- **Repo start/end tag** (`start-epNN` / `end-epNN`)
- **Title & thumbnail ideas**

---

## 8. Tone & teaching principles

- **Decisions, not dictation.** Always say *why* — "Core stays BCL-only so the domain never depends
  on a vendor SDK" beats silently adding a project reference.
- **Honesty about uncertainty.** When the repo says *verify the SDK API against the installed
  package*, model that on camera (open the docs, check IntelliSense). It teaches a real engineering
  habit and ages well even if a package changes.
- **Show the failure, then the fix** for the signature gotchas (the `TurnToken` hang in Ep 11 is
  the standout). Watching it hang `Idle` and then explaining the one-line fix is unforgettable
  teaching.
- **Keep Core pure on camera.** Let the `DependencyRuleTests` "catch" an accidental package
  reference once — a memorable demonstration of architecture-as-tests.

[← Index](README.md) · [Next: Production & Recording Setup →](01-production-and-recording-setup.md)
