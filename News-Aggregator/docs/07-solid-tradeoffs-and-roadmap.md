# 7. SOLID, Tradeoffs & Roadmap

[← Aspire Topology & Docker](06-aspire-topology-and-docker.md) · [Index](README.md)

## 7.1 SOLID applied per layer

| Principle | How the design honors it |
| --- | --- |
| **S — Single Responsibility** | Each agent does one job (summarize / categorize / rank / edit). Each source connector handles one source. Workflow construction, model-provider creation, aggregation/merge, and HTTP fetching are separate classes. No God class. |
| **O — Open/Closed** | New sources = add an `INewsSource` adapter (no change to aggregation). New providers = add an `IChatModelProvider`/factory branch (no change to agents/workflows). New agents = add to the workflow's agent set. |
| **L — Liskov Substitution** | Any `INewsSource`, any `IChatClient` provider, any `AIAgent` is substitutable; the application service depends only on the contracts and behaves correctly for any conforming implementation. |
| **I — Interface Segregation** | Small, role-specific ports (`INewsSource`, `IEnrichmentWorkflow`, `IEditorialWorkflow`, `IChatModelProvider`, `IDigestCache`) rather than one fat "service" interface. |
| **D — Dependency Inversion** | Core defines ports; Infrastructure implements them; the Web composition root binds them. Core never references a concrete framework. `IChatClient`/`ChatClientAgent` appear only in Infrastructure. |

Supporting rules (from the brief), and where they're enforced:
- **DI everywhere / no static service locators** → single composition root in
  `Web/Program.cs`; constructor injection + injected factories only. ([§2.5](02-architecture-and-project-structure.md))
- **No business logic in UI** → Blazor components call `DigestApplicationService`; they
  render and forward events only. ([§2](02-architecture-and-project-structure.md))
- **No framework coupling in Domain** → `Core` is BCL-only. ([§2.3](02-architecture-and-project-structure.md))

## 7.2 Key tradeoff analysis

### A. Microsoft Agent Framework vs hand-rolled `Microsoft.Extensions.AI`
- **Chosen:** Agent Framework for orchestration (`AgentWorkflowBuilder` concurrent +
  sequential) on top of M.E.AI for model access.
- **Why:** the brief mandates it, and `BuildConcurrent`/`BuildSequential` deliver
  fan-out/fan-in + pipeline semantics, event streaming, and HITL hooks we'd otherwise
  reimplement.
- **Cost:** an evolving framework (RC/GA version skew — see [index](README.md)). We
  isolate it entirely behind Core ports so a future API change touches Infrastructure
  only.

### B. Concurrent vs Sequential (why both)
- Enrichment steps are **independent** → Concurrent for latency + diverse perspectives.
- Editorial steps are **dependent** → Sequential for correct, reproducible ordering.
- Forcing one pattern everywhere would either add latency or corrupt ordering. See the
  per-pattern fit table in [§3.6](03-agent-orchestration-design.md).

### C. Blazor Server vs WASM/SPA
- **Chosen:** Blazor Server. Streams `AgentResponseUpdateEvent` to the browser over
  SignalR with no separate API, single deployable, simplest clean-arch fit.
- **Cost:** stateful server connections; less suited to massive scale. Acceptable for
  an MVP; the application service boundary makes a later API/SPA extraction cheap.

### D. In-process workflows vs durable workflows
- **Chosen:** `InProcessExecution` (in-memory). Simple, fast, no infrastructure.
- **Cost:** no persistence/recovery across restarts. The Durable Task extension can add
  checkpointing to the same `WorkflowBuilder` graphs later ✅ — deferred (7.4).

### E. Ollama client: OllamaSharp (Microsoft-recommended) + first-party container
- **Chosen:** `OllamaSharp` (`OllamaApiClient` as `IChatClient`) for the client —
  wrapped by `AsAIAgent`/`ChatClientAgent` — and a **first-party** Aspire generic
  container (`AddContainer`) for hosting. **No Community Toolkit.**
- **Why:** `OllamaSharp` is the client used by the official .NET AI *Chat with a local
  AI model* quickstart; `Microsoft.Extensions.AI.Ollama` is **deprecated**. Because
  `OllamaApiClient` is an `IChatClient`, agents/workflows are unaffected by the choice.
- **Cost:** one third-party dependency (`OllamaSharp`, pinned `5.4.25`). Aspire has no
  first-party Ollama *hosting* integration, so hosting is a generic container (slightly
  more wiring). *Alternative:* run Ollama on the host and configure the endpoint.

### F. `IChatClient` in Core vs strictly BCL-only Core
- **Chosen:** strictly BCL-only Core; M.E.AI abstractions stay in Infrastructure.
- **Tradeoff:** a tiny amount of mapping boilerplate (Core's neutral ports → M.E.AI
  types) in exchange for a Domain that's provably framework-free and trivially testable.
- **Reversible:** if the team later treats M.E.AI *abstractions* as a stable "language
  extension," they can be promoted into Core without disturbing callers.

### G. No database in MVP
- **Chosen:** in-memory + optional distributed cache only.
- **Tradeoff:** no history/persistence; simplest possible MVP. A repository port can be
  introduced later behind the existing service boundary without touching agents/UI.

## 7.3 Risks & mitigations

| Risk | Likelihood | Mitigation |
| --- | --- | --- |
| Agent Framework preview→GA API churn | Med | All framework usage behind Infrastructure adapters; pin & align `Microsoft.Agents.AI.*` versions; smoke test in CI. |
| Local model can't do tool/structured calls | Med | Default to tool-capable models (`llama3.2`/`qwen3`); validate capability at startup; degrade to plain-text summary. |
| Ollama latency on CPU | High (without GPU) | Bound enrichment parallelism; small models by default; document GPU passthrough. |
| OpenRouter endpoint option name differs by SDK | Med | Flagged ⚠️; alternative = custom `HttpClient` `BaseAddress`. ([§4.4](04-model-providers-and-byok.md)) |
| Source rate limits / flaky feeds | Med | `Microsoft.Extensions.Http.Resilience` handlers; skip & log bad feeds; cache responses. |
| Prompt-injected/malicious feed content | Med | Treat article text as untrusted; constrain agent outputs; never execute tool calls from content without the (deferred) HITL approval gate. |
| Secret leakage | Low/High impact | Keys only in user-secrets/env/Aspire params; never logged; options validation requires key only when provider = OpenRouter. |

## 7.4 Post-MVP roadmap

1. **Persistence** — add an `IArticleRepository` port + EF Core (or SQLite) adapter for
   history, dedupe memory, and a read model.
2. **Durable workflows** — adopt the Durable Task extension to checkpoint/resume the
   enrichment and editorial graphs ✅.
3. **More sources** — Reddit, Dev.to/Hashnode connectors (new `INewsSource` adapters).
4. **Richer orchestration** — Handoff (route specialist topics), Group Chat (editorial
   debate), Magentic (manager-led) ✅, all already available in the framework.
5. **Human-in-the-loop** — enable `ApprovalRequiredAIFunction` + `RequestInfoEvent`
   approval gates for sensitive tool calls ✅.
6. **Semantic features** — embeddings (`IEmbeddingGenerator`) for semantic dedupe and
   topic clustering; vector store.
7. **Auth & personalization** — accounts, per-user feeds and saved digests.
8. **Deployment** — Aspire manifest → container/Kubernetes; secrets vault; CI/CD; the
   shared ServiceDefaults becomes its own project once a 2nd service exists.

## 7.5 Definition of done for the MVP build (when code begins)

- Five projects exactly; `Core` references no framework package (CI-checked).
- One "Refresh" runs Concurrent enrichment then Sequential editorial and renders a
  categorized, ranked, summarized digest from HN + RSS.
- Provider switch (Ollama ↔ OpenRouter) and adding an RSS feed are config-only.
- `dotnet run` on AppHost starts Web + Ollama (+ optional Redis) with the dashboard.
- Tests: Core unit tests, adapter tests with fakes, one workflow smoke test green.
- Every Agent Framework/Aspire package version pinned from nuget.org and aligned.

[← Aspire Topology & Docker](06-aspire-topology-and-docker.md) · [Index](README.md)
