# 09 — Finale & Channel Growth (Episode 16 + beyond)

[← Phase 6](08-phase-6-production-and-aspire.md) · [Index](README.md)

The grand finale that ties the series together, plus how to repurpose the playlist for reach and
where the series can grow next.

---

## Episode 16 — Full demo, recap & roadmap (~26–34 min)

**Hook.** "We did it. Let's run the whole thing end-to-end, look back at the decisions that made it
hold together, and map where you can take it from here."

**Learning objectives:**
- See the finished app run end-to-end from a single `dotnet run`.
- Switch **Ollama ↔ OpenRouter** live to prove the model-portability claim.
- Consolidate the architecture lessons: ports & adapters, determinism-in-Core, the two orchestration
  patterns, and tests-as-guardrails.
- Understand the roadmap and how the app was *designed* to grow (`docs/07`).

**Prerequisites.** Eps 1–15 (the whole app). Tag: `end-ep16` (= finished `main`).

**Segment 1 — The grand demo.** `dotnet run` on the AppHost → Aspire dashboard up, model healthy →
open the app → **Refresh** → narrate the live agent progress → the categorized, ranked, summarized
digest → filter by category and tag. Then flip `Models:Provider` to `OpenRouter` (key from
user-secrets) and refresh again — *same code, frontier model* — closing the loop on `docs/01 §1.7`
success criteria ("switching Ollama ↔ OpenRouter is config-only", "adding an RSS feed is
config-only"). Optionally add a new RSS feed in config live to prove that one too.

**Segment 2 — Recap: why it held up.** Walk back through the architecture with the benefit of
hindsight (this is the "senior takeaways" segment):
- **Ports & adapters / Core BCL-only** meant the model SDKs, news APIs, and UI were all swappable —
  and a test (`DependencyRuleTests`) *enforced* it the whole way.
- **Determinism in Core, LLM in Infrastructure** — the *total* `EnrichedItemAssembler` and the pure
  `DigestComposer`/`DigestFilter` kept every non-deterministic edge testable and crash-proof.
- **The right orchestration for the job** — concurrent fan-out/fan-in for independent enrichment;
  sequential pipeline for dependent editorial steps (`docs/03 §3.6`).
- **Verify-the-SDK discipline** — the `TurnToken` gotcha is why we never guess an API.
- **Aspire** turned a multi-process AI system into one `dotnet run`.

**Segment 3 — Roadmap (straight from `docs/07` / `docs/01 §1.6`).** What was deliberately deferred,
and the seams already left for it:
- **Redis distributed cache** — the `IDigestCache` port + the commented `.UseDistributedCache()` in
  `ChatClientFactory` are the seam (`InMemoryDigestCache` → `DistributedDigestCache`).
- **Embeddings / vector search / semantic de-dupe** — currently URL/title dedupe only; an
  `IEmbeddingGenerator` adapter is the natural next port.
- **Human-in-the-loop approval** — the framework supports `ApprovalRequiredAIFunction` /
  `RequestPort` pause points (`docs/03 §3.7`).
- **More orchestration patterns** — Handoff, Group Chat, Magentic (`docs/03 §3.7`).
- **Durable / checkpointed workflows** via the Durable Task extension (`docs/03 §3.7`, `docs/07`).
- **Persistence / accounts / personalization** — explicitly out of MVP (`docs/01 §1.6`).

**Demo.** The full end-to-end run + the live provider swap (above).

**Recap & CTA.** Thank viewers; point to the repo (all `start/end` tags), the playlist, and invite
them to pick one roadmap item and extend the app. Tease any follow-up (a roadmap item as a bonus
episode — see below).

**Visuals/B-roll.** Montage of the journey (one shot per phase), the `docs/02` architecture diagram
with every box now "lit," the Aspire dashboard, the roadmap as a checklist.

**Title/thumbnail.** `I Built a Multi-Agent AI App in .NET — Full Demo & What I'd Add Next` ·
"FINISHED + ROADMAP."

---

## Repurposing the playlist for reach

The long-form build is the core asset; slice it for discovery:

- **Shorts / Reels (vertical, <60s):** the `TurnToken` failure→fix (Ep 11); "run an LLM locally in
  60 seconds" (Ep 7); the live provider swap (Ep 8/16); the dependency-rule test going red (Ep 1);
  the live agent-progress UI (Ep 13). These are the visually punchy moments.
- **Standalone "deep-dive" cuts:** *"Concurrent vs. Sequential AI Agents in .NET"* (Eps 11+12
  recut), *"Microsoft.Extensions.AI in 10 minutes"* (Ep 7 distilled). These rank on their own search
  terms and funnel into the full playlist.
- **A written companion** (blog/README) per phase, linking the episode and the repo tag — captures
  the search traffic video can't.
- **Carousel/diagram posts** of the `docs/02`/`docs/03` diagrams with a one-line takeaway.

> **Rationale.** Each suggested clip is an *actual* moment in the build, not invented hype — the
> failure→fix, the local LLM, the config-only swap, and the live UI are the genuinely novel,
> shareable beats for an intermediate .NET audience.

---

## Where the series can grow (bonus-episode candidates)

Each roadmap item is a ready-made follow-up that reuses the established format and the existing
seams — natural "Season 2" content:

1. **Add Redis caching** — swap `InMemoryDigestCache` for `DistributedDigestCache`, add Redis to the
   Aspire AppHost, enable `.UseDistributedCache()` on the chat pipeline.
2. **Semantic de-dupe with embeddings** — introduce an `IEmbeddingGenerator` adapter and replace
   URL/title dedupe with vector similarity.
3. **Human-in-the-loop** — add tool-approval pauses with `ApprovalRequiredAIFunction` /
   `RequestPort`.
4. **A new orchestration pattern** — rebuild a slice with Handoff or Group Chat to contrast with the
   concurrent/sequential patterns from this series.
5. **Durable workflows** — make the pipeline checkpointable/resumable via the Durable Task extension.

---

## Final production reminders

- Keep the **per-episode template** identical across all 17 videos (see
  [`00-strategy-and-audience.md`](00-strategy-and-audience.md)) so the series feels like one course.
- Pin the **repo tag** for each episode and keep the dual-screen reference open while you build (see
  [`01-production-and-recording-setup.md`](01-production-and-recording-setup.md)).
- Ground every explanation in `News-Aggregator/docs/01`…`07` so what you say on camera matches the
  real design intent — and flag any "verify against the installed package" API the same way the repo
  does. Teaching the *discipline of verifying* is one of this series' most durable lessons.

[← Phase 6](08-phase-6-production-and-aspire.md) · [Index](README.md)
