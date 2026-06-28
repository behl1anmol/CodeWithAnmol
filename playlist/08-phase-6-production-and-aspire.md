# 08 — Phase 6: Production-Readiness & Aspire (Episodes 14–15)

[← Phase 5](07-phase-5-blazor-ui.md) · [Index](README.md) · [Next: Finale →](09-finale-and-channel-growth.md)

**Phase accent color suggestion:** indigo/graphite (the "ops" color). **Goal of the phase:** make
the app *start and run like a product* — a health check that reports whether the model provider is
reachable, and a **.NET Aspire** AppHost that boots the model, the app, telemetry, and a dashboard
with a single `dotnet run`. Capped by the end-to-end smoke test that proves the whole pipeline.

---

## Episode 14 — Health checks & startup validation (P5) (~22–28 min)

**Hook.** "Nothing's worse than a clean-looking app that can't reach its model. We'll add a health
check that tells you *instantly* whether your provider is up — without ever leaking your API key."

**Learning objectives:**
- Implement `ModelProviderHealthCheck : IHealthCheck` that probes the **active** provider (Ollama
  endpoint reachability / OpenRouter reachability).
- Register it via `AddHealthChecks().AddCheck<…>("model-provider")` so it surfaces on `/health` and
  the Aspire dashboard.
- Keep the probe fast, side-effect-free, and **secret-safe** (the key never appears in any result).

**Prerequisites.** Eps 7–8 (providers + `ModelOptions`). Independent of the workflows. Maps to repo
**P5**. Tag: `start-ep14` → `end-ep14`.

**Talk segment.** Why a provider health check matters for an AI app (`docs/04`, `docs/05 §5.3`): the
model is an external dependency, so surfacing its reachability at startup turns a confusing "it just
hangs" into a clear red light. Explain the per-provider probe: Ollama → a cheap `GET` to the
endpoint / `/api/tags` (200 = healthy); OpenRouter → base-URL reachability (don't burn a completion;
**never** include the key). Use `IHttpClientFactory` with a bounded timeout; health checks run
out-of-band so they never block startup.

**Hands-on build (in order):**
1. `Infrastructure/HealthChecks/ModelProviderHealthCheck.cs` — read `ModelOptions`, branch on
   `Provider`, probe with a short timeout, map success/failure to `HealthCheckResult` with a
   non-secret description.
2. Register the check in `InfrastructureServiceCollectionExtensions` / `Program.cs`; confirm
   `MapDefaultEndpoints()` exposes it. Add `ValidateOnStart()` options validation (recap the Ep 8
   OpenRouter-key rule).
3. `Tests/HealthChecks/ModelProviderHealthCheckTests.cs` with `FakeHttpMessageHandler`.

**Tests to show (selective — yes).** Ollama healthy (200) / unhealthy (timeout or non-200);
OpenRouter reachable/unreachable; and a pointed assertion that **the key never appears** in any
`HealthCheckResult` text. That last test is a great security-mindset teaching beat.

**Demo.** Hit `/health` with Ollama up (Healthy), stop Ollama, hit it again (Unhealthy with a clear,
key-free message). Foreshadow that the Aspire dashboard will show this as a status light next
episode.

**Gotchas / verify moments.** Never log/surface the API key. The probe must not block startup
(out-of-band). No Core change (health checks are Infrastructure). Verify the
`Microsoft.Extensions.Diagnostics.HealthChecks` `IHealthCheck` surface against the installed version.

**Visuals.** A red/green status light mockup tied to the provider being up/down.

**Repo tag.** `start-ep14` → `end-ep14`.

**Title/thumbnail.** `Health Checks for Your AI Provider in .NET (Key-Safe)` · "PROVIDER STATUS."

---

## Episode 15 — Aspire AppHost + Ollama bootstrap + smoke test (P6) (~30–35 min)

**Hook.** "One command. We'll boot the model, the web app, telemetry, and a live dashboard with a
single `dotnet run` — and prove the whole pipeline works end-to-end."

**Learning objectives:**
- Understand **.NET Aspire**: the AppHost app model, the dashboard, service discovery, OpenTelemetry,
  and health surfacing.
- Bootstrap Ollama from the AppHost with the **CommunityToolkit** integration:
  `AddOllama("ollama").WithDataVolume().AddModel("llama3.2")`, and `WaitFor(...)` so the Web app
  doesn't start until the model is ready.
- Wire `ServiceDefaultsExtensions` (telemetry/resilience/health) into the Web app.
- Add the end-to-end `DigestPipelineSmokeTests` proving collect → enrich → compose works through the
  **real** workflows (with fakes).

**Prerequisites.** Ep 11 + Ep 12 (real workflows); benefits from Ep 14 (provider health). Maps to
repo **P6**. Tag: `start-ep15` → `end-ep15`. **Build the AppHost with the real SDK on PATH** (it
needs `Aspire.AppHost.Sdk` from `global.json`).

**Talk segment.** What Aspire gives you (`docs/06`): a code-first app model that declares your
resources (the Web app, the Ollama container, optional Redis) and wires connection strings, env
vars, health, and OpenTelemetry into a single dashboard — so local dev mirrors a real deployment.
Then the **Ollama bootstrap** decision (`docs/06 §6.1`, P6): the repo uses the CommunityToolkit
Ollama hosting integration to *pull the model automatically* and persist it in a data volume across
runs, with `WaitFor` gating the app on model readiness. Explain why this matters: first-run UX —
without it, the first digest fails because the model isn't downloaded yet (which the Ep 13 error
path and the Ep 14 health check already handle gracefully).

**Hands-on build (in order):**
1. `Web/Extensions/ServiceDefaultsExtensions.cs` — `AddServiceDefaults()` /
   `MapDefaultEndpoints()` (OpenTelemetry exporters, service discovery, resilience, health). Note the
   deliberate tradeoff (`docs/02 §2.1`, `docs/07`): ServiceDefaults lives *inside* Web rather than a
   6th project, because there's only one runnable service.
2. `AppHost/AppHost.cs` — `AddOllama("ollama").WithDataVolume().AddModel("llama3.2")`; add the Web
   project as a resource, inject the Ollama endpoint (`Models__Ollama__Endpoint`), `.WaitFor(model)`,
   `.WithExternalHttpEndpoints()`.
3. `Tests/Workflows/DigestPipelineSmokeTests.cs` — wire `DigestApplicationService` over
   `FakeNewsSource`(s) + the **real** `ConcurrentEnrichmentWorkflow` + `SequentialEditorialWorkflow`
   driven by `FakeAgentFactory`/`FakeChatClient` (canned valid JSON) + `InMemoryDigestCache`. Assert
   a non-empty, categorized, score-ordered `Digest`; cache written once; progress stages fire in
   order (`collecting → enriching → composing → done`). Offline + deterministic.

**Tests to show (selective — yes, the smoke test).** Run `DigestPipelineSmokeTests` — the single test
that exercises the *entire* pipeline through the real workflows with fakes. It's the capstone proof
and a satisfying green check before the finale.

**Demo (payoff — leads into the finale).** From the AppHost: `dotnet run`. Open the **Aspire
dashboard**: see the Ollama container and the Web app come up, the model-provider health light go
green, traces/logs flowing via OpenTelemetry. Then open the app and run a real refresh end-to-end.
One command, whole system.

**Gotchas / verify moments.** Build the AppHost with the **real SDK on PATH** (don't build it from a
neutral cwd — it consumes the Aspire msbuild SDK). The Ollama bootstrap API is community/Aspire
surface — **verify it against the installed `CommunityToolkit.Aspire.Hosting.Ollama` /
`Aspire.Hosting.AppHost 13.4.x`** before relying on it (`docs/06`, P6 explicitly says confirm with
`microsoft_docs_search`/the Aspire API — model that on camera). AppHost is infrastructure
composition only — no business logic.

**Visuals/B-roll.** The Aspire dashboard (resources, health, traces); the `docs/06` topology
diagram.

**Repo tag.** `start-ep15` → `end-ep15`.

**Title/thumbnail.** `One 'dotnet run' to Boot Your Whole AI App (.NET Aspire)` · "ONE COMMAND."

---

### Phase 6 wrap (say this on camera at the end of Ep 15)

"The app now starts like a product: one command boots the model, the web app, health checks, and a
full telemetry dashboard — and a single smoke test proves the whole pipeline end-to-end. Everything
is built. Next time: the grand demo, a look back at why the architecture held up, and where you can
take this next."

[← Phase 5](07-phase-5-blazor-ui.md) · [Index](README.md) · [Next: Finale →](09-finale-and-channel-growth.md)
