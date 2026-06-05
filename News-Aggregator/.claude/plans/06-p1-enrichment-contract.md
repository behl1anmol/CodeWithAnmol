# Plan 06 — P1: Enrichment output contract & Core mapper

> **Date:** 2026-06-05 · **Branch:** `claude/news-aggregator-mvp-p1-WtnE7`
> **Prompt:** P1 in [`../prompts/mvp-completion-prompts.md`](../prompts/mvp-completion-prompts.md)
> **Prerequisites:** none. **Unblocks:** P2 (concurrent enrichment), P6 (smoke test).

## Context

Closes gaps **G1** (agent-output → `EnrichedItem` mapping was undefined) and the enrichment
half of **G2** (Summarizer/Categorizer/Ranker prompts were placeholders with no structured-output
contract). Defines one pure, framework-free, **total** contract for turning the three enrichment
agents' replies into a valid `EnrichedItem`, and refines the three prompts to emit exactly that
contract. The Editor prompt is intentionally left for P3. No workflow/DI changes (P2–P4).

## Files

- **New** `Core/Application/Enrichment/Taxonomy.cs` — closed category set
  (`AI, Security, Cloud, Devtools, Web, Data, Hardware, Other`) + `Normalize(string?)`
  (case-insensitive → canonical casing; blank/unknown → `Other`). Single source of truth shared
  by the assembler and (later, P4) the UI filter. BCL-only.
- **New** `Core/Application/Enrichment/EnrichmentOutputs.cs` — plain POCOs `CategoryResult`
  (`Category`, `Tags`) and `RelevanceResult` (`Score`, `Reason`). `Score` is `double?` so a missing
  field is distinguishable from a genuine `0`. Double as `System.Text.Json` deserialization targets.
- **New** `Core/Application/Enrichment/EnrichedItemAssembler.cs` — pure `Assemble(NewsItem,
  summaryText, categorizerJson, rankerJson)` mapper. **Total**: never throws on bad LLM output.
  - Defensive parse: `TryExtractJsonObject` scans for the first balanced `{…}` block (string/escape
    aware, so braces inside string values don't break matching), tolerating code fences / prose;
    `JsonSerializer.Deserialize` wrapped in try/catch (`JsonSerializerDefaults.Web`, matching the
    source adapters' casing tolerance).
  - Invariant guarantees: blank summary → trimmed `Content` snippet (capped 500 chars) → `Title`;
    category via `Taxonomy.Normalize`; tags trimmed, blanks dropped, deduped case-insensitively,
    capped at 5; score missing/`NaN`/out-of-range `[0,1]` → `RelevanceScore.Zero`, else
    `new RelevanceScore(score, reason)`.
- **Modified** `Infrastructure/Agents/AgentInstructions.cs` — Summarizer (plain text, 2–3 neutral
  sentences, no markdown), Categorizer (strict minified JSON `{"category","tags"}`, taxonomy built
  from `Taxonomy.Categories` — single source of truth), Ranker (strict minified JSON
  `{"score","reason"}`). Editor unchanged (P3).
- **New** `Tests/Application/EnrichedItemAssemblerTests.cs` — clean/fenced/prose-wrapped JSON;
  braces-in-strings; missing/non-taxonomy category → `Other`; case-insensitive canonicalization;
  missing/out-of-range/unparseable/boundary scores; summary → content → title fallback; snippet cap;
  tag clean/dedupe/cap; garbage everywhere still yields a valid item; null item throws; blank reason
  → null.

## Constraints honoured

- **Core stays BCL-only.** `System.Text.Json` ships in the shared framework, so it adds no package
  reference — `DependencyRuleTests` stays green (verified).
- No change to `EnrichedItem` / `RelevanceScore`. Additive only. No DI/workflow/UI edits.

## Verification (SDK 10.0.300 installed to `/tmp/dotnet` in this sandbox)

```
dotnet build NewsAggregator.Core/...            # 0 warn / 0 err
dotnet build NewsAggregator.Infrastructure/...  # 0 warn / 0 err  (-warnaserror)
dotnet build NewsAggregator.Tests/...           # 0 warn / 0 err  (-warnaserror)
dotnet test  NewsAggregator.Tests/...           # Passed: 143, Failed: 0
```

Test count rose **119 → 143** (new assembler cases incl. theory rows); none broken;
`DependencyRuleTests` green. AppHost/Web builds not required for P1 (untouched).
