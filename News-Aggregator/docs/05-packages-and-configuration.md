# 5. Packages & Configuration Strategy

[← Model Providers & BYOK](04-model-providers-and-byok.md) · [Index](README.md) · [Next: Aspire Topology & Docker →](06-aspire-topology-and-docker.md)

> **Pinned versions are in [§5.0](#50-microsoft-libraries-that-must-be-version-pinned)**
> (and summarized in the [index](README.md)). Agent Framework packages are all `1.8.0`;
> `Microsoft.Extensions.AI` packages are `10.6.0` — two independent version lines. The
> per-project tables below name packages; §5.0 is the source of truth for versions.

## 5.0 Microsoft libraries that MUST be version-pinned

Pin these **exact** versions, managed centrally via `Directory.Packages.props`
(Central Package Management) so they can't drift between projects. Note the **two
independent version lines**: Agent Framework is on its own `1.x` line while
`Microsoft.Extensions.AI` follows the `10.x` (.NET 10) line — they are *not* meant to
match each other.

**Group 1 — Microsoft Agent Framework — all on `1.8.0`:**
| Package | Version |
| --- | --- |
| `Microsoft.Agents.AI` | `1.8.0` |
| `Microsoft.Agents.AI.Workflows` | `1.8.0` |
| `Microsoft.Agents.AI.OpenAI` | `1.8.0` |

**Group 2 — Microsoft.Extensions.AI — all on `10.6.0`:**
| Package | Version |
| --- | --- |
| `Microsoft.Extensions.AI` | `10.6.0` |
| `Microsoft.Extensions.AI.Abstractions` | `10.6.0` *(usually transitive; pin if referenced directly)* |

**Group 3 — .NET Aspire — pin to one Aspire release line ⚠️ (confirm the line for .NET 10):**
| Package | Version |
| --- | --- |
| `Aspire.Hosting.AppHost` | ⚠️ pin |
| `Aspire.AppHost.Sdk` | ⚠️ pin |
| `Microsoft.Extensions.ServiceDiscovery` | ⚠️ pin |
| `Aspire.Hosting.Redis` *(optional)* | ⚠️ pin |
| `Aspire.StackExchange.Redis.DistributedCaching` *(optional)* | ⚠️ pin |

**Local-LLM client (third-party, but Microsoft-recommended):**
| Package | Version |
| --- | --- |
| `OllamaSharp` | v4+ ⚠️ pin — provides `OllamaApiClient` as `IChatClient` |

> **Deprecated / not used:** `Microsoft.Extensions.AI.Ollama` (**deprecated** — the
> official .NET AI *Chat with a local AI model* quickstart uses `OllamaSharp` instead),
> `CommunityToolkit.Aspire.Hosting.Ollama`, `CommunityToolkit.Aspire.OllamaSharp`.

## 5.1 Package recommendations by project

Legend: ✅ verified name · ⚠️ verify version/exact name · ❓ uncertain (alternative given).

### Core (`NewsAggregator.Core`)
| Package | Status | Why |
| --- | --- | --- |
| *(none — BCL only)* | ✅ | Enforces "no framework coupling in Domain." Core defines ports + entities only. |

### Infrastructure (`NewsAggregator.Infrastructure`)
| Package | Status | Purpose |
| --- | --- | --- |
| `Microsoft.Agents.AI` | ✅ / ⚠️ ver | `AIAgent`, `ChatClientAgent`. |
| `Microsoft.Agents.AI.Workflows` | ✅ / ⚠️ ver | `AgentWorkflowBuilder` (`BuildConcurrent`/`BuildSequential`), `WorkflowBuilder`, `InProcessExecution`, workflow events. |
| `Microsoft.Agents.AI.OpenAI` | ✅ / ⚠️ ver | `AsAIAgent()` bridge for OpenAI-compatible (OpenRouter). |
| `Microsoft.Extensions.AI` | ✅ | `IChatClient`, `ChatClientBuilder`, `UseFunctionInvocation`, `UseOpenTelemetry`. |
| `Microsoft.Extensions.AI.Abstractions` | ✅ | Abstractions (`ChatMessage`, `ChatResponse`); usually transitive via `Microsoft.Extensions.AI`. |
| `OllamaSharp` (v4+) | ✅ | `OllamaApiClient` implementing `IChatClient` for local LLM. Microsoft-recommended Ollama client; `Microsoft.Extensions.AI.Ollama` is deprecated. No Community Toolkit needed. |
| `OpenAI` | ✅ | Official OpenAI SDK; OpenRouter via base-URL override (⚠️ verify option). |
| `Microsoft.Extensions.Http` | ✅ | `IHttpClientFactory` for source connectors. |
| `System.ServiceModel.Syndication` | ✅ | RSS/Atom parsing for the RSS connector. |
| `Microsoft.Extensions.Caching.Abstractions` | ✅ | `IDistributedCache`/`IMemoryCache` for digest/source caching. |

> **No Community Toolkit.** The local-LLM client is `OllamaSharp`'s `OllamaApiClient`,
> registered by the Infrastructure provider adapter and reading its endpoint/model from
> configuration (which the Aspire AppHost injects — see [§6](06-aspire-topology-and-docker.md)).

### Web (`NewsAggregator.Web`)
| Package | Status | Purpose |
| --- | --- | --- |
| *(SDK)* `Microsoft.NET.Sdk.Web` + Blazor Server | ✅ | UI host + DI composition root. |
| `Microsoft.Extensions.ServiceDiscovery` | ✅ | Service discovery (ServiceDefaults). |
| `OpenTelemetry.Extensions.Hosting` + OTLP exporter | ✅ / ⚠️ ver | Telemetry → Aspire dashboard. |
| `Microsoft.Extensions.Http.Resilience` | ✅ | Standard resilience handlers for outbound HTTP. |
| `Aspire.StackExchange.Redis.DistributedCaching` | ⚠️ ver | Only if the optional Redis cache is enabled. |

### AppHost (`NewsAggregator.AppHost`)
| Package | Status | Purpose |
| --- | --- | --- |
| `Aspire.Hosting.AppHost` | ✅ / ⚠️ ver | Aspire app model host. |
| `Aspire.AppHost.Sdk` (SDK ref) | ✅ | Aspire AppHost build SDK. |
| `Aspire.Hosting.Redis` | ⚠️ ver | Optional Redis resource for distributed cache. |

> **Ollama hosting** uses **first-party** Aspire only: model the Ollama runtime as a
> generic container via `builder.AddContainer("ollama", "ollama/ollama")` (in
> `Aspire.Hosting.AppHost`), or point config at a host-installed Ollama. **No
> Community Toolkit package.** See [§6.1](06-aspire-topology-and-docker.md).

### Tests (`NewsAggregator.Tests`)
| Package | Status | Purpose |
| --- | --- | --- |
| `xunit` + `xunit.runner.visualstudio` | ✅ | Test framework. |
| `Microsoft.NET.Test.Sdk` | ✅ | Test host. |
| `NSubstitute` *(or `Moq`)* | ✅ | Fakes for Core ports. |
| `Microsoft.Extensions.AI` test fakes / custom `IChatClient` stub | ✅ | Deterministic agent tests without a live model. |

## 5.2 Configuration strategy

Principles: **Options pattern everywhere**, secrets never in source, environment
overrides layered, Aspire supplies endpoints/connection strings.

### Configuration sources (precedence, low → high)
1. `appsettings.json` (committed defaults).
2. `appsettings.{Environment}.json` (e.g. Development → Ollama default).
3. **User Secrets** (local dev keys — e.g. OpenRouter key) — never committed.
4. **Environment variables** (containers / CI).
5. **Aspire parameters & connection strings** (injected by AppHost: Ollama endpoint
   + model, Redis connection).

### Strongly-typed options (bound in the composition root)

```jsonc
// appsettings.json — ILLUSTRATIVE schema (not final), no secrets committed.
{
  "Sources": {
    "HackerNews": { "Enabled": true, "MaxItems": 30, "Story": "topstories" },
    "Rss": {
      "Enabled": true,
      "Feeds": [
        "https://feeds.arstechnica.com/arstechnica/index",
        "https://www.theverge.com/rss/index.xml"
      ]
    }
  },
  "Models": {
    "Provider": "Ollama",                 // "Ollama" | "OpenRouter"
    "Ollama":     { "Endpoint": "http://localhost:11434", "DefaultModel": "llama3.2" },
    "OpenRouter": { "Endpoint": "https://openrouter.ai/api/v1", "DefaultModel": "openai/gpt-4o-mini" },
    "AgentModels": {                      // optional per-agent overrides
      "Summarizer": null, "Categorizer": null, "Ranker": null, "Editor": null
    }
  },
  "Enrichment": { "MaxDegreeOfParallelism": 4 },
  "Cache": { "Provider": "Memory" }       // "Memory" | "Redis"
}
```

```jsonc
// Secrets via user-secrets / env / Aspire param — NEVER in appsettings.
// dotnet user-secrets set "Models:OpenRouter:ApiKey" "sk-or-..."
{ "Models": { "OpenRouter": { "ApiKey": "<from secret store>" } } }
```

### Binding (composition root)
```csharp
// Illustrative.
builder.Services.AddOptions<SourceOptions>().BindConfiguration("Sources").ValidateOnStart();
builder.Services.AddOptions<ModelOptions>().BindConfiguration("Models").ValidateOnStart();
builder.Services.AddOptions<EnrichmentOptions>().BindConfiguration("Enrichment");
```

### Secret handling rules
- `OpenRouter:ApiKey` is **required only when** `Models:Provider == "OpenRouter"`;
  options validation enforces this at startup (`ValidateOnStart`).
- No key is ever logged. The provider factory reads it from `IOptions<ModelOptions>`,
  sourced from user-secrets (dev) or an Aspire parameter / env var (containers).
- Ollama needs no secret.

## 5.3 Validation & fail-fast
- `ValidateOnStart()` on options → misconfiguration fails at boot, not mid-request.
- Source connectors validate feed URLs at startup; invalid feeds are logged and skipped.
- A startup health check pings the active model provider (Ollama endpoint / OpenRouter
  reachability) so the Aspire dashboard shows provider health immediately.

[← Model Providers & BYOK](04-model-providers-and-byok.md) · [Index](README.md) · [Next: Aspire Topology & Docker →](06-aspire-topology-and-docker.md)
