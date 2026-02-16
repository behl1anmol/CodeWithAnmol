# Episode Tags — the `teaching` branch map

> The mechanical prep sheet the production plan asks for in
> [`01-production-and-recording-setup.md §2.4`](01-production-and-recording-setup.md). It maps every
> episode to its git tag pair and the files that episode introduces, so recording from a clean
> "empty start" is point-and-shoot.

All snapshots live on the orphan **`teaching`** branch (a clean, linear, episode-ordered history,
separate from `main`). Each episode `NN` has two tags:

- **`start-epNN`** — the repo with everything from earlier episodes present and this episode's files
  not yet added (its clean "empty start").
- **`end-epNN`** — the repo after this episode's code is filled in.

By construction **`end-epNN` ≡ `start-ep(NN+1)`** (the same commit, two names), so the series is one
continuous build with no gaps:

```
start-ep01 → end-ep01 ≡ start-ep02 → … → end-ep15 ≡ end-ep16  (= finished app)
```

`end-ep15` is the finished app; `end-ep16` is the same commit (the finale/trailer demo the finished
app, they add no code). Episode 0 (trailer) also demos `end-ep16`.

## Recording from a tag

```bash
git switch --detach start-epNN     # clean starting point on the recording screen
# …record the build…
git switch teaching                # compare your result to the reference
git diff end-epNN                  # ~empty if you tracked the finished code
```

## How these tags were produced

Reverse-stripped from the finished `main` (the single source of truth for file **content**), using
[`02-curriculum-overview.md §2`](02-curriculum-overview.md) as the authoritative map of which file
belongs to which episode. The tip (`end-ep15`) is **identical** to `main`'s `News-Aggregator/src`.
The `teaching` branch carries `News-Aggregator/src` (stripped per episode) plus `News-Aggregator/docs`
(constant reference); it omits the `playlist/`, `TimeProvider/`, and `.claude/` folders.

## The map

| Ep | Tags | Introduces (primary files) |
|----|------|----------------------------|
| 0 | *(demo `end-ep16`)* | — trailer, no build |
| 1 | `start-ep01` → `end-ep01` | `.slnx`, 5 `.csproj`, `Directory.*.props`, `global.json`, Blazor shell, minimal `Program.cs`/`AppHost.cs` |
| 2 | `start-ep02` → `end-ep02` | `Core/Domain/*`, `Enrichment/Taxonomy.cs`, domain tests, `Architecture/DependencyRuleTests.cs` |
| 3 | `start-ep03` → `end-ep03` | `Application/Ports/*`, `Services/DigestApplicationService.cs`, `AgentProgress.cs`, `Configuration/*Options.cs`, `Caching/InMemoryDigestCache.cs`, orchestration tests + `ApplicationFakes`/`RecordingProgress` |
| 4 | `start-ep04` → `end-ep04` | `Sources/HackerNewsSource.cs`, `HackerNewsSourceTests.cs`, `Fakes/FakeHttpMessageHandler.cs` |
| 5 | `start-ep05` → `end-ep05` | `Sources/RssNewsSource.cs`, `RssNewsSourceTests.cs` |
| 6 | `start-ep06` → `end-ep06` | `Sources/GitHubNewsSource.cs`, `Services/NewsAggregationService.cs`, GitHub + aggregation tests, `Fakes/FakeNewsSource.cs` |
| 7 | `start-ep07` → `end-ep07` | `Models/IChatClientFactory.cs`, `OllamaChatModelProvider.cs`, `ChatClientFactory.cs` *(Ollama branch only)* |
| 8 | `start-ep08` → `end-ep08` | `Models/OpenRouterChatModelProvider.cs`, `ChatClientFactory.cs` *(full)* |
| 9 | `start-ep09` → `end-ep09` | `Agents/IAgentFactory.cs`, `AgentFrameworkAgentFactory.cs`, `AgentInstructions.cs` |
| 10 | `start-ep10` → `end-ep10` | `Enrichment/EnrichmentOutputs.cs`, `EnrichedItemAssembler.cs`, `EnrichedItemAssemblerTests.cs` *(P1)* |
| 11 | `start-ep11` → `end-ep11` | `Workflows/ConcurrentEnrichmentWorkflow.cs`, its tests, `Fakes/FakeChatClient.cs`/`FakeAgentFactory.cs` *(P2)* |
| 12 | `start-ep12` → `end-ep12` | `Editorial/DigestComposer.cs`, `EditorIntroParser.cs`, `Workflows/SequentialEditorialWorkflow.cs`, their tests, `Fakes/FixedTimeProvider.cs` *(P3)* |
| 13 | `start-ep13` → `end-ep13` | `Pages/Digest.razor`, `Editorial/DigestFilter.cs`, `DigestFilterTests.cs`; `Program.cs` + `InfrastructureServiceCollectionExtensions.cs` wired up *(P4)* |
| 14 | `start-ep14` → `end-ep14` | `HealthChecks/ModelProviderHealthCheck.cs`, its tests; health check registered in `Program.cs`/DI *(P5)* |
| 15 | `start-ep15` → `end-ep15` | `AppHost/AppHost.cs` *(full)*, `Extensions/ServiceDefaultsExtensions.cs`, `DigestPipelineSmokeTests.cs` *(P6)* |
| 16 | *(= `end-ep16`)* | — finale, no build |

## Reconstruction notes (where the tags deviate from a naive file split)

These were forced by **compilation correctness** (the environment that built the tags had no .NET
SDK, so see *Verification* below) and are worth knowing when prepping:

- **`DependencyRuleTests` lands at `end-ep02`, not `end-ep01`.** It anchors on `typeof(NewsItem)`, so
  it cannot compile until the domain exists. Episode 1 still writes/demos it live; the tag that first
  *compiles* it is `end-ep02`.
- **The web composition root is wired at Episode 13.** `Program.cs` stays a minimal Blazor host
  through Ep1–Ep12 (those episodes demo via tests/console harnesses, per the phase docs), and
  `InfrastructureServiceCollectionExtensions.cs` (`AddInfrastructure`) is introduced whole at Ep13
  when the UI first needs it. `Program.cs` then grows: +health check at Ep14, +`AddServiceDefaults`/
  `MapDefaultEndpoints` at Ep15.
- **`ChatClientFactory` has an Ollama-only form at Ep7**, gaining the OpenRouter branch at Ep8.
- **Project files (`.csproj`) carry their final package references from Ep1.** Unused packages don't
  break a build, and `Core.csproj` stays zero-package throughout (so `DependencyRuleTests` passes at
  every stage). The "add this package" beat can still be shown on camera.

## Verification (run locally — needs the .NET 10 SDK on PATH)

The tags were created without a compiler available, so build-verify locally before recording:

```bash
# each start tag should COMPILE (stubs throw only at runtime):
for n in $(seq -w 1 15); do git switch --detach "start-ep$n" && dotnet build News-Aggregator/src; done

# each end tag should pass the tests that exist by that episode:
for n in $(seq -w 1 15); do git switch --detach "end-ep$n" && dotnet test News-Aggregator/src; done
```

Build the **AppHost** with the real SDK on PATH (it consumes `Aspire.AppHost.Sdk` from `global.json`).
Continuity and tip-fidelity are already verified: `end-epNN ≡ start-ep(NN+1)` for all `N`, and
`git diff end-ep15 main -- News-Aggregator/src` is empty.
