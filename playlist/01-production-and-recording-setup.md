# 01 — Production & Recording Setup

[← Strategy](00-strategy-and-audience.md) · [Index](README.md) · [Next: Curriculum Overview →](02-curriculum-overview.md)

This is the operational playbook: how to use the dual-screen setup, how to give every episode a
clean "empty start" and a known "finished end" via git, the tooling that keeps recordings smooth,
and how to avoid dead air while builds compile and models think.

---

## 1. The dual-screen workflow

You have two screens. Assign them fixed roles and never swap:

| Screen | Role | What's open |
|---|---|---|
| **Reference screen** (not recorded) | Your "answer key." | The **finished** `News-Aggregator/` repo, opened at the exact files this episode builds, plus the relevant `docs/` chapter. |
| **Recording screen** (captured by OBS) | The live build. | The **episode-start copy** (stripped repo at `start-epNN`), an integrated terminal, and a browser tab for demos. |

**Discipline that keeps it watchable:**

- **Identical file paths on both screens.** Because the recording copy is derived from the finished
  repo (next section), namespaces and paths match exactly — you glance at the reference and type
  the same path. No "where does this go?" fumbling on camera.
- **Pre-read the reference, then teach — don't transcribe.** Read the finished file on the
  reference screen during prep, understand *why* it's written that way (the `docs/` explain it),
  then build it on the recording screen in your own words. The goal is teaching, not copying.
- **Keep the reference `docs/` chapter open** for the talk segment so your explanation matches the
  real design intent (e.g. `docs/03` for the workflow episodes).
- The reference screen is your safety net for the gotchas — when you intentionally show the
  `TurnToken` hang in Ep 11, the fix is right there if you lose the thread.

---

## 2. The code-stripping strategy (the most important setup step)

Your format needs **"a copy of the projects without any code"** as each episode's starting point.
Doing this by hand for 16 episodes is error-prone. Instead, **derive every episode boundary from
the one finished repo using git tags.** This guarantees the start state actually compiles up to
that point and the end state matches the finished app.

### 2.1 The tag scheme

For each episode `NN` define two commits on a dedicated teaching branch:

- `start-epNN` — the repo with everything from episodes `< NN` present, and **this episode's files
  removed or stubbed** (`throw new NotImplementedException()` / empty class bodies / TODO markers).
- `end-epNN` — the repo after this episode's code is filled in. **`end-epNN` is identical to
  `start-ep(NN+1)`**, so the series is one continuous build with no gaps or overlaps.

```
start-ep01 → end-ep01 ≡ start-ep02 → end-ep02 ≡ start-ep03 → … → end-ep16 ≡ finished app
```

### 2.2 How to produce the tags (recommended: reverse-strip from the finish)

The finished repo is the source of truth, so build the timeline **backwards** — it's far less
error-prone than re-typing forward:

1. Start from the finished `main`. Tag it `end-ep16`.
2. To create `start-epNN`, take `end-epNN` and **revert just this episode's additions** — delete
   the new files, and replace method bodies the episode implements with the *scaffold* that existed
   before (the repo already uses `NotImplementedException` scaffolds, e.g. the workflows pre-P2/P3
   — see `.claude/prompts/mvp-completion-prompts.md`). Commit and tag.
3. Repeat down to `start-ep01` (a bare 5-project skeleton).
4. Verify each `start-epNN` **compiles** (scaffolds compile; unimplemented behavior just throws)
   and each `end-epNN` **passes the tests that existed by that episode**.

> **Why this works and is safe.** The repo was literally built as a sequence of self-contained
> prompts (P1–P6) with per-prompt snapshots in `News-Aggregator/.claude/checkpoints/`. Those
> checkpoints already mark natural boundaries for Episodes 10–15; you're extending the same idea to
> the foundational episodes. Reverse-stripping means you never invent code — you only remove what
> the finished repo already contains.

### 2.3 Recording from a tag

At the top of each shoot:

```bash
git switch --detach start-epNN     # clean, known starting point on the recording screen
# ...record the build...
git switch teaching                # (after recording) compare your result to end-epNN
git diff end-epNN                  # should be ~empty if you tracked the reference
```

Pin the `start-epNN`/`end-epNN` tag links in each video's description and a comment so viewers can
check out the exact same starting point and follow along.

### 2.4 Practical guardrails

- Keep a small `episodes.md` (or reuse `02-curriculum-overview.md`) mapping each episode → its tag
  pair → the files it touches, so prep is mechanical.
- Strip **secrets** out of every start tag (no OpenRouter key); the app already uses user-secrets,
  so this is natural.
- The `.slnx`, `Directory.Packages.props`, and `Directory.Build.props` exist from `start-ep01`
  onward — you don't re-create solution plumbing each episode, you only add the episode's code.

---

## 3. Tooling & on-screen setup

| Concern | Recommendation | Why |
|---|---|---|
| **Capture** | OBS Studio; record the editor + terminal scene at 1080p/60 (or 1440p). | Free, reliable, scene-based for quick demo cutaways. |
| **Editor** | One IDE for the whole series (VS / VS Code / Rider). Large font (≥16pt), high-contrast theme, zoom hotkey ready. | Readability on phones; consistency. |
| **Terminal** | Integrated terminal, big font, cleared between steps. Set `export PATH=$HOME/.dotnet:$PATH` once. | Matches the repo's documented build setup. |
| **Live reload** | `dotnet watch` for the Blazor episodes (13+). | Instant UI feedback without manual restarts. |
| **Model** | **Pre-pull `llama3.2`** (`ollama pull llama3.2`) before recording; keep the Ollama container warm. | Avoids multi-minute first-token waits on camera. |
| **Determinism for demos** | Use the repo's **fakes** (`FakeChatClient`, `FakeAgentFactory`, `FakeNewsSource`, `FakeHttpMessageHandler`) for test/demo segments that must be repeatable. | The repo's tests are deterministic and offline by design — reuse that for clean takes. |
| **Diagrams** | Export the Mermaid diagrams from `docs/02`/`docs/03` as images for the talk segments. | They already express the architecture and the two workflows accurately. |

---

## 4. Dead-air management

The two things that create dead air are **builds** and **model calls**. Plan around both:

- **Builds:** keep talking through the first build of an episode (explain the next step while it
  compiles); pre-warm with one build before you hit record so NuGet restore isn't on camera.
- **First model call:** pre-pull and warm the model; if a real call is still slow, narrate what the
  agent is doing, or cut to a prepared diagram and return when the response lands.
- **Long enrichment demos (Ep 11):** show a **small batch** (3–5 articles) live for honesty, and
  have a pre-recorded full-batch run ready to cut to if you want the "wow" without the wait.
- **Tests are instant and deterministic** — they're your reliable "show it works" beat when a live
  model would be too slow. This is *why* "show tests selectively" is the right call for pacing.

---

## 5. Per-video production checklist

**Prep (before recording):**

- [ ] `git switch --detach start-epNN` on the recording screen; confirm it builds (`dotnet build`).
- [ ] Finished files for this episode open on the reference screen; relevant `docs/` chapter open.
- [ ] Ollama running with `llama3.2` pulled and warmed (for episodes that hit a model).
- [ ] OBS scenes ready (editor, terminal, browser/dashboard); font size checked on a phone preview.
- [ ] Talk-segment diagram/slides exported and queued.
- [ ] Secrets out of the working copy; demo data/fakes ready for deterministic beats.

**Record:** follow the episode script (hook → recap → talk → build → tests → demo → recap).

**After recording:**

- [ ] `git diff end-epNN` ≈ empty (you matched the reference); commit your built result.
- [ ] Capture the final demo cleanly (re-take just the demo if the live one stalled).
- [ ] Note real chapter timestamps for the description.

**Publish:**

- [ ] Title + thumbnail per [`00-strategy-and-audience.md`](00-strategy-and-audience.md).
- [ ] Description with chapters, stack line, repo link, and the `start-epNN`/`end-epNN` tags.
- [ ] Add to playlist in order; pin a comment with the repo tag and "what's next."

[← Strategy](00-strategy-and-audience.md) · [Index](README.md) · [Next: Curriculum Overview →](02-curriculum-overview.md)
