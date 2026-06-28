# 07 — Phase 5: Blazor Server UI (Episode 13)

[← Phase 4](06-phase-4-agents-and-orchestration.md) · [Index](README.md) · [Next: Phase 6 →](08-phase-6-production-and-aspire.md)

**Phase accent color suggestion:** green (the "it's alive" color). **Goal of the phase:** put the
whole pipeline behind a Blazor Server page that triggers a real refresh, **streams live agent
progress to the browser**, and lets the user filter the digest by category/tag — delivering the core
user stories from `docs/01 §1.3`. This is the second big payoff peak of the series.

---

## Episode 13 — Blazor Server UI: live progress + filtering (P4) (~30–35 min)

**Hook.** "Time to make it real. One click, and you'll watch our four agents process the news *live*
in the browser — then filter the finished digest by category and tag."

**Learning objectives:**
- Wire `Digest.razor` (Interactive Server) to call `DigestApplicationService.RefreshDigestAsync`.
- Stream **live agent progress** to the browser using `IProgress<AgentProgress>` → `StateHasChanged`
  — no custom SignalR hub needed (Blazor Server already streams DOM diffs over its SignalR circuit).
- Keep **business logic out of the UI**: filtering goes in a pure `DigestFilter` helper, unit-tested.
- Render each item fully: title link, relevance score, summary, category, tags.

**Prerequisites.** Ep 11 + Ep 12 (so `RefreshDigestAsync` returns a real `Digest`). Maps to repo
**P4**. Tag: `start-ep13` → `end-ep13`.

**Talk segment.** Two ideas to explain before coding:
1. **How live progress works in Blazor Server (`docs/02 §2.4`).** Create
   `new Progress<AgentProgress>(p => { _stage = p.Stage; … ; InvokeAsync(StateHasChanged); })` and
   pass it into `RefreshDigestAsync`. Each agent event updates component state and re-renders; Blazor
   Server ships the diff over its existing SignalR circuit. We get a live "watch the agents work"
   experience *for free* — emphasize we are **not** building a custom hub.
2. **No business logic in the component.** Filtering by category/tag is a *pure function* over the
   digest, so it lives in `DigestFilter` (testable) and the markup just calls it. This is the same
   discipline as the assembler/composer — UI renders, helpers decide.

**Hands-on build (in order):**
1. `Core/Application/Editorial/DigestFilter.cs` — pure `(Digest, category?, tag?) → filtered Digest`;
   no filter selected → passthrough; empty result handled.
2. `Web/Components/Pages/Digest.razor` (`@rendermode InteractiveServer`):
   - inject `IDigestApplicationService` + `ILogger`;
   - a **Refresh** button → `RefreshDigestAsync(progress, ct)` with a component-scoped
     `CancellationToken` (cancel on dispose);
   - render the current `Stage` / `ProcessedCount` / `TotalCount` live;
   - **category** and **tag** dropdowns (categories from the Core `Taxonomy`; tags from the digest)
     → apply `DigestFilter`;
   - render sections: heading + optional Editor intro; per item: title (linked), relevance score
     (`0.00`), summary, tags;
   - a generic `try/catch` that logs and shows a friendly "Could not generate digest — check the
     model provider" instead of a blank page (remove the scaffold `NotImplementedException` catch).
3. Composition-root wiring so the page resolves the real services.
4. `Tests/Application/DigestFilterTests.cs`.

**Tests to show (selective — yes).** `DigestFilterTests`: filter by category; by tag; no-filter
passthrough; empty result. Pure and instant — the right thing to test for a UI feature (we verify
the *page* by running it, not with bUnit, per `docs`/P4 guidance).

**Demo (payoff — second peak).** Run the Web app (`dotnet watch`), click **Refresh**, and narrate as
the stage text moves `collecting → enriching → composing → done` with the counter climbing — the
agents working *live*. Then the finished categorized digest renders; pick a category, pick a tag,
watch it filter instantly. This is the "it's a real product" moment — make it shine.

**Gotchas / verify moments.**
- Call `StateHasChanged` via `InvokeAsync` from the progress callback (it fires off the UI thread).
- Don't introduce a custom SignalR hub — Blazor Server already streams; adding one is the classic
  over-engineering trap here (`docs/07 §7.2-C`).
- Keep *all* transform logic in `DigestFilter`; the `.razor` should have no `if/else` business rules.
- A model/provider failure must degrade to the friendly message (this also covers the "model not
  pulled yet" case that Ep 15 addresses).

**Visuals/B-roll.** Screen capture of the live stage updates; a quick before/after of the digest
filtering. Optionally picture-in-picture of the terminal logs while the UI updates.

**Repo tag.** `start-ep13` → `end-ep13`.

**Title/thumbnail.** `Live AI Agent Progress in Blazor (No Custom SignalR)` · "LIVE IN THE BROWSER."

---

### Phase 5 wrap (say this on camera at the end of Ep 13)

"The app is alive: click a button, watch real agents work, read a curated digest, filter it. It runs
on your machine right now. The last phase is about making it *start like a product* — health checks
and a single `dotnet run` that boots the model, the app, and a full observability dashboard."

[← Phase 4](06-phase-4-agents-and-orchestration.md) · [Index](README.md) · [Next: Phase 6 →](08-phase-6-production-and-aspire.md)
