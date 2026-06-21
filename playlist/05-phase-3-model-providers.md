# 05 — Phase 3: Model Providers / `Microsoft.Extensions.AI` (Episodes 7–8)

[← Phase 2](04-phase-2-news-sources.md) · [Index](README.md) · [Next: Phase 4 →](06-phase-4-agents-and-orchestration.md)

**Phase accent color suggestion:** purple. **Goal of the phase:** introduce
`Microsoft.Extensions.AI` and its central abstraction, **`IChatClient`**, then implement two
interchangeable providers — **Ollama** (local, default) and **OpenRouter** (cloud, BYOK) — proving
the app can swap models with a config change and no code change. This is the foundation the agents
(Phase 4) stand on.

> **Teaching throughline:** `IChatClient` is to LLMs what `DbConnection` is to databases — one
> abstraction, many backends. Build a provider once, run it against a model on your laptop or a
> frontier model in the cloud.

---

## Episode 7 — Ollama provider & the `IChatClient` pipeline (~26–32 min)

**Hook.** "Let's run a large language model entirely on your own machine — no API key, no cloud —
and talk to it through the same `IChatClient` interface we'll use for everything else."

**Learning objectives:**
- Understand `Microsoft.Extensions.AI` and the `IChatClient` abstraction.
- Use **OllamaSharp**'s `OllamaApiClient` (which *implements* `IChatClient`) to talk to a local
  model (`llama3.2`).
- Build the **`IChatClient` middleware pipeline**: `.AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build()`.
- Implement `OllamaChatModelProvider : IChatModelProvider` and `ChatClientFactory` (Core port →
  concrete `IChatClient`).

**Prerequisites.** Ep 3 (the `IChatModelProvider` port + `ModelOptions`/`ChatModelDescriptor`). Tag:
`start-ep07` → `end-ep07`.

**Talk segment.** What `Microsoft.Extensions.AI` is and why it exists: a vendor-neutral set of
abstractions (`IChatClient`, `ChatMessage`, `ChatResponse`) so your code doesn't bind to one model
SDK. Then the **pipeline/middleware** idea — `IChatClient` composes like ASP.NET middleware:
`UseFunctionInvocation()` enables tool/function calling, `UseOpenTelemetry()` instruments every call
for the Aspire dashboard later (`docs/01 §1.8`, `docs/04`). Show how OllamaSharp's client *is* an
`IChatClient`, so it drops straight into the pipeline.

**Hands-on build (in order):**
1. `Infrastructure/Models/IChatClientFactory.cs` — `IChatClient Create(ChatModelDescriptor)`.
2. `Infrastructure/Models/ChatClientFactory.cs` — for Ollama, construct `OllamaApiClient(endpoint,
   model)`, then `raw.AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build(serviceProvider)`.
   (Note the commented `.UseDistributedCache()` — a deliberate post-MVP seam; mention it for the
   roadmap.)
3. `Infrastructure/Models/OllamaChatModelProvider.cs` — implement `IChatModelProvider`: from
   `ModelOptions.Ollama`, return a `ChatModelDescriptor` (provider, model id, endpoint) per
   `AgentRole`, honoring per-agent model overrides (`AgentModels`).
4. Minimal DI registration in the composition root to resolve the active provider from config.

**Tests to show (selective — light).** A test that `ChatClientFactory` produces a client for an
Ollama descriptor; assert the provider maps roles/overrides correctly. (The real model isn't called
in tests — deterministic/offline.)

**Demo (payoff).** First real LLM round-trip: send `"Summarize: <an article from Ep 6>"` to the
Ollama `IChatClient` and print the reply. The app is now *thinking* — locally. (Pre-pull & warm
`llama3.2`; keep the article short so the demo is quick.)

**Gotchas / verify moments.** `IChatClient` is a `Microsoft.Extensions.AI` type and **must stay out
of Core** (only Infrastructure references it) — re-run `DependencyRuleTests` to prove Core is still
clean. Confirm the exact OllamaSharp version/API against `Directory.Packages.props` (pinned
`5.4.25`) — model the "verify, don't guess" habit. The codebase deliberately omits
`ConfigureAwait(false)`; match that style.

**Visuals.** A pipeline diagram: `OllamaApiClient → UseFunctionInvocation → UseOpenTelemetry →
IChatClient`.

**Repo tag.** `start-ep07` → `end-ep07`.

**Title/thumbnail.** `Run a Local LLM in .NET with Ollama + Microsoft.Extensions.AI` · "LOCAL LLM."

---

## Episode 8 — OpenRouter BYOK provider (~22–28 min)

**Hook.** "Same code, frontier models. We'll add a cloud provider so you can switch from your laptop
model to GPT-class models by changing one config value."

**Learning objectives:**
- Implement `OpenRouterChatModelProvider : IChatModelProvider` using the **OpenAI SDK** against
  OpenRouter's OpenAI-compatible endpoint.
- Adapt the OpenAI chat client to `IChatClient` with `.AsIChatClient()`
  (`Microsoft.Extensions.AI.OpenAI`).
- Manage the API key securely with **user-secrets** (BYOK) — never committed, validated at startup.
- Switch providers via `Models:Provider` with **zero code change**.

**Prerequisites.** Ep 7 (the factory + pipeline). Tag: `start-ep08` → `end-ep08`.

**Talk segment.** OpenRouter exposes an OpenAI-compatible API, so we reuse the `OpenAI` SDK: point
`OpenAIClient` at `https://openrouter.ai/api/v1` with the key, call `GetChatClient(model)`, then
`.AsIChatClient()` to slot into the *same* pipeline from Ep 7. Emphasize the `docs/01 §1.6` /
`docs/04` BYOK story: the key comes from user-secrets/env, never source; we validate at startup
(`ValidateOnStart`) so a missing key fails fast and clearly.

**Hands-on build (in order):**
1. `Infrastructure/Models/OpenRouterChatModelProvider.cs` — from `ModelOptions.OpenRouter`, build a
   `ChatModelDescriptor` per role.
2. Extend `ChatClientFactory` — for the OpenRouter branch, construct the OpenAI client against the
   OpenRouter endpoint + key, `.AsIChatClient()`, then the same `.AsBuilder().Use…().Build()`.
3. Composition root: select the provider by `ModelOptions.Provider`; add options validation that
   *requires* the key when `Provider == OpenRouter`.
4. Set the key via `dotnet user-secrets set "Models:OpenRouter:ApiKey" …` on camera (show the
   `UserSecretsId` already in the Web `.csproj`).

**Tests to show (selective — light).** Startup-validation test: `Provider == OpenRouter` with no key
fails validation; with a key it binds. (No live cloud call in tests.)

**Demo (payoff).** Run the *same* summarize demo from Ep 7, but flip `Models:Provider` from `Ollama`
to `OpenRouter` — identical code path, a cloud model answers. Drive home the abstraction win.

**Gotchas.** **Never** log or print the API key — show that the health check (Ep 14) and any
diagnostics deliberately avoid it. Verify the `.AsIChatClient()` extension and OpenAI SDK version
against the installed package (`OpenAI 2.10.0`, `Microsoft.Extensions.AI.OpenAI 10.6.0` in
`Directory.Packages.props`). OpenRouter sometimes needs referer/title headers — mention if relevant
to your account.

**Visuals.** A toggle graphic: one `IChatClient` consumer, two providers behind it, switch = config.

**Repo tag.** `start-ep08` → `end-ep08`.

**Title/thumbnail.** `Swap Local ↔ Cloud LLMs in .NET with One Config Change` · "BYOK + SWAP."

---

### Phase 3 wrap (say this on camera at the end of Ep 8)

"We can now talk to a model — local or cloud — through one clean interface, and switch between them
without touching code. But a raw chat client isn't an *agent*. Next phase, the heart of the series:
we turn these clients into a team of specialized agents and orchestrate them."

[← Phase 2](04-phase-2-news-sources.md) · [Index](README.md) · [Next: Phase 4 →](06-phase-4-agents-and-orchestration.md)
