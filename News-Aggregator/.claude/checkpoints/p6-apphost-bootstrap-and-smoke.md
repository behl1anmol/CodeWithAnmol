# Checkpoint — P6: AppHost Ollama model bootstrap + end-to-end smoke test

**Prompt:** P6 (`../prompts/mvp-completion-prompts.md`) · **Prereq:** P2 ✅ + P3 ✅ (benefits from P5 ✅) ·
**Result:** 209 passed, 0 failed; Infrastructure + Web + AppHost build 0 warn / 0 err (SDK 10.0.300).
**Outcome:** **MVP complete** — `docs/07 §7.5` fully satisfied.

## What it does
1. **Model bootstrap.** A single `dotnet run` on the AppHost can now serve a digest on the first run:
   the configured Ollama model is pulled on startup. The generic `AddContainer("ollama", …)` is
   replaced by the **CommunityToolkit Ollama hosting** integration
   (`AddOllama(...).WithDataVolume().AddModel("llama3.2")`), and the Web app `WaitFor(model)` so it
   only starts once the pull completes. The chat **client** is unchanged (OllamaSharp in
   Infrastructure) — the toolkit is used for *hosting only*.
2. **End-to-end smoke test.** `DigestPipelineSmokeTests` drives collect → enrich → compose → cache
   through the **real** coordinator + **real** Agent Framework workflows, offline (only the model is
   faked), proving the whole pipeline is wired.

## Decision asked this session (constraint B/C)
- **Bootstrap approach → CommunityToolkit `AddModel`** (chosen by the user) over (a) a documented
  operator step + readiness gate, or (b) an Aspire `OnResourceReady` hook POSTing `/api/pull`.
  **This deliberately reverses** the repo's prior "No Community Toolkit / Lean MVP" stance
  (`Directory.Packages.props` + `docs/06 §6.1`), so those docs were updated to record it. Rationale
  the user accepted: cleanest code, auto-pull on startup; the deviation is documented.

## Files
| File | Change |
|---|---|
| `src/Directory.Packages.props` | **+** `CommunityToolkit.Aspire.Hosting.Ollama 13.4.0`; reworded the "deliberately NOT referenced" block (only the *client* toolkit `CommunityToolkit.Aspire.OllamaSharp` stays out). |
| `src/NewsAggregator.AppHost/NewsAggregator.AppHost.csproj` | **+** `<PackageReference Include="CommunityToolkit.Aspire.Hosting.Ollama" />`. |
| `src/NewsAggregator.AppHost/AppHost.cs` | **rewrote** the ollama block: `AddOllama("ollama").WithDataVolume()` + `AddModel("llama3.2")`; inject endpoint via `ollama.Resource.PrimaryEndpoint`; `WaitFor(model)`. |
| `src/NewsAggregator.Tests/Workflows/DigestPipelineSmokeTests.cs` | **new** — 3 tests (see below). |
| `docs/05`, `docs/06 §6.1` | updated to reflect the CommunityToolkit hosting adoption (client unchanged). |

## Verified APIs (against installed `CommunityToolkit.Aspire.Hosting.Ollama` 13.4.0)
Read from `~/.nuget/packages/communitytoolkit.aspire.hosting.ollama/13.4.0/lib/net8.0/*.xml` + assembly:
- `AddOllama(this IDistributedApplicationBuilder, string name, int? port = null)` → `IResourceBuilder<OllamaResource>`.
- `WithDataVolume(this IResourceBuilder<OllamaResource>, string? name = null, bool isReadOnly = false)`.
- `AddModel(this IResourceBuilder<IOllamaResource>, string modelName)` (+ `(…, string name, string modelName)`).
- `OllamaResource.PrimaryEndpoint` (verified property) — used instead of guessing a `GetEndpoint("…")` name.
- `WithEnvironment<T>(IResourceBuilder<T>, string, EndpointReference)` exists in `Aspire.Hosting` 13.4.2.
- `WaitFor(model)` blocks the Web app until the model resource is ready (pull complete).

## Smoke test design (offline, deterministic)
- Real `NewsAggregationService([FakeNewsSource])` → real `ConcurrentEnrichmentWorkflow(factory, Options.Create(EnrichmentOptions))`
  → real `SequentialEditorialWorkflow(factory, FixedTimeProvider)` → `InMemoryDigestCache(new MemoryCache(...))`,
  all wired through real `DigestApplicationService`.
- **Per-title-aware fake `IChatClient`** (`TitleAwareChatClient`, via `FakeAgentFactory.clientFactory`):
  Categorizer/Ranker parse the `Title:` line from the workflow prompt and return per-article
  category/score JSON, so the digest has multiple sections with varied scores; Summarizer/Editor use
  canned `FakeChatClient` replies. 4 articles → Security(0.9,0.5) / AI(0.7) / Cloud(0.3).
- **Asserts:** non-empty + every section category ∈ `Taxonomy.Categories`; 4 items total; section order
  `[Security, AI, Cloud]` + top-score-descending; Security items `[A, C]`; editor intro mapped;
  `GeneratedAt == FixedNow`; digest round-trips from cache under `"digest:latest"`; progress stages
  fire in order `collecting < enriching < composing < done`.

## Gotchas hit (fixed)
- **`EnrichedItem.Item` is the `NewsItem`** — title is `enriched.Item.Title`, not `enriched.Item.Item.Title`
  (caught before build).
- **Endpoint accessor** — rather than guess the toolkit's endpoint name, used the verified
  `OllamaResource.PrimaryEndpoint` property with the existing `WithEnvironment(string, EndpointReference)`
  overload, keeping the `Models__Ollama__Endpoint` env name/shape identical (Infrastructure untouched).
- **`MemoryCache` in tests** — available transitively via the Infrastructure project reference; no new
  test package needed.

## Verification
- `dotnet build` Infrastructure / Web / **AppHost** (real SDK on PATH) → all **0/0**.
- `dotnet test` → **209 passed, 0 failed** (baseline 206 + 3 smoke tests).
- Runtime model-pull (`dotnet run`) is environment-dependent (Docker + network) → documented, not
  asserted by the offline suite.
- Reviewed by `cavecrew-reviewer` (caveman plugin).

## Next
**None — MVP complete.** All of P1–P6 merged; `docs/07 §7.5` satisfied. Post-MVP roadmap (Redis,
persistence, durable workflows, HITL) is out of scope per the analysis §6.
