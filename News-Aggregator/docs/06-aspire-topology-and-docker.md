# 6. Aspire Topology & Docker Strategy

[← Packages & Configuration](05-packages-and-configuration.md) · [Index](README.md) · [Next: SOLID, Tradeoffs & Roadmap →](07-solid-tradeoffs-and-roadmap.md)

## 6.1 Aspire app model (AppHost)

`NewsAggregator.AppHost` is the composition root for **infrastructure** (not business
logic). It declares the resources, wires endpoints/connection strings, and provides the
**Aspire dashboard**, **OpenTelemetry**, and **health checks** for free.

```mermaid
graph TD
    subgraph AppHost["NewsAggregator.AppHost (Aspire app model)"]
        Web["webfrontend<br/>(Blazor Server)"]
        Ollama["ollama<br/>(container) ✅⚠️ Community Toolkit"]
        Model["llama3.2<br/>(OllamaModelResource) ⚠️"]
        Redis["redis (optional)<br/>distributed cache ⚠️ver"]
    end
    Dash["Aspire Dashboard<br/>(traces / logs / metrics)"]

    Ollama --> Model
    Model -->|"connection string<br/>Endpoint=...;Model=..."| Web
    Redis -->|connection string| Web
    Web --> Dash
    Ollama --> Dash
```

### Illustrative AppHost wiring
```csharp
// Illustrative — verify Community Toolkit API/version before use.
var builder = DistributedApplication.CreateBuilder(args);

// Local LLM — Community Toolkit Ollama hosting ⚠️
var ollama = builder.AddOllama("ollama")          // ⚠️ CommunityToolkit.Aspire.Hosting.Ollama
                    .WithDataVolume();             // persist pulled models across runs
var llama  = ollama.AddModel("llama3.2");         // ⚠️ OllamaModelResource

// Optional cache
var redis = builder.AddRedis("redis");            // ⚠️ verify version

builder.AddProject<Projects.NewsAggregator_Web>("webfrontend")
       .WithReference(llama)                       // injects Endpoint=...;Model=... ✅ format
       .WithReference(redis)
       .WithExternalHttpEndpoints();

builder.Build().Run();
```

> **Verified Aspire facts:**
> - Aspire's Ollama integration is **Community Toolkit**, not first-party:
>   `CommunityToolkit.Aspire.Hosting.Ollama` (hosting) and
>   `CommunityToolkit.Aspire.OllamaSharp` (client) ✅.
> - Since the toolkit's 9.0 release, models are **resources** (`OllamaModelResource`)
>   and the connection string is a real `Endpoint=<...>;Model=<...>` format ✅.
> - **Flag:** pin the toolkit version compatible with your Aspire/.NET 10 line ⚠️.
>   *Alternative if the toolkit lags:* model Ollama as a plain container
>   (`AddContainer("ollama", "ollama/ollama")`) and pass the endpoint via env/config.

### ServiceDefaults
Per [§2](02-architecture-and-project-structure.md), the ServiceDefaults extension
(OpenTelemetry wiring, health-check endpoints, `Microsoft.Extensions.ServiceDiscovery`,
standard HTTP resilience) lives **inside `NewsAggregator.Web`** to keep the project
count at five. `Program.cs` calls `builder.AddServiceDefaults()`.

## 6.2 Observability
- The `IChatClient` pipeline uses `.UseOpenTelemetry()` ✅, so every model call emits
  traces/metrics visible in the Aspire dashboard.
- Workflow execution surfaces `AgentResponseUpdateEvent`/`WorkflowOutputEvent` ✅; these
  are logged and also streamed to the Blazor UI via SignalR for live progress.
- Health checks: Web liveness/readiness + a provider-reachability check (Ollama/OpenRouter).

## 6.3 Docker strategy

Two distinct concerns: **(a) running dependencies as containers during dev** (Aspire
manages this automatically) and **(b) producing a deployable image of the Web app.**

### (a) Dependencies — managed by Aspire
- **Ollama** runs as a container image (`ollama/ollama`) declared in AppHost. A
  **data volume** persists pulled models so they aren't re-downloaded each run.
- **Redis** (optional) runs as a container only when caching is set to Redis.
- No hand-written `docker compose` is required for local dev — the AppHost is the
  single entry point (`dotnet run` on AppHost starts everything).

### (b) Web app image
- `NewsAggregator.Web` ships a **multi-stage Dockerfile** (SDK build stage →
  chiseled/`aspnet` runtime stage) for size and security.
- Alternatively use **.NET SDK container publish**
  (`dotnet publish -t:PublishContainer`) to avoid maintaining a Dockerfile ✅;
  pick one and standardize (recommend SDK container publish for the MVP).
- The image is provider-agnostic: it talks to whatever `Models:Provider` resolves to,
  using endpoints/keys injected via environment variables.

### Deployment manifest
- For non-local deployment, Aspire can **generate a manifest** describing resources,
  which tools translate to compose/Kubernetes ⚠️ (verify the current
  `aspire`/`azd` manifest workflow for your Aspire version). MVP target is **local
  Aspire only**; container/manifest publishing is a roadmap item.

```mermaid
graph LR
    subgraph Dev["Local dev (single `dotnet run` on AppHost)"]
        A["AppHost"] --> W["Web (process)"]
        A --> O["Ollama (container + volume)"]
        A --> R["Redis (container, optional)"]
    end
    subgraph Img["Deployable artifact"]
        WI["Web container image<br/>(SDK container publish)"]
    end
    W -. publish .-> WI
```

### GPU note (Ollama)
Local inference is CPU-bound by default. For acceptable latency with larger models,
enable GPU passthrough to the Ollama container (e.g. NVIDIA container runtime). This is
an environment/runtime concern ⚠️ (host-dependent) — document it for operators but keep
the MVP working on CPU with small models (`llama3.2`, `qwen3`).

## 6.4 Local-dev vs container tradeoffs

| | Ollama as Aspire container | Ollama installed on host |
| --- | --- | --- |
| Setup | One `dotnet run`; reproducible; volume-persisted models. | Manual install; shared across projects; uses host GPU easily. |
| Isolation | Clean, disposable. | Pollutes host but faster GPU access. |
| MVP default | **Yes** (reproducibility wins). | Supported via config (point `Models:Ollama:Endpoint` at host). |

[← Packages & Configuration](05-packages-and-configuration.md) · [Index](README.md) · [Next: SOLID, Tradeoffs & Roadmap →](07-solid-tradeoffs-and-roadmap.md)
