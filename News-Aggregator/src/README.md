# NewsAggregator — solution scaffold

MVP scaffold for the **Multi-Source Tech News Aggregator** (.NET 10). This is a
clean, compilable, extensible **foundation** — ports, DI wiring, configuration,
Aspire app model, providers, and tests are in place; business logic, news
collection, and agent workflows are intentionally **not** implemented yet
(stubs throw `NotImplementedException` with `TODO(scaffold)` notes).

The full architecture is documented in [`../docs`](../docs/README.md).

## Projects (Clean Architecture, 5 projects)

| Project | Role |
| --- | --- |
| `NewsAggregator.Core` | Domain + application ports/services. **Zero package references** (BCL only). |
| `NewsAggregator.Infrastructure` | Adapters: sources, model providers (Ollama/OpenRouter), agents, workflows, cache. |
| `NewsAggregator.Web` | Blazor Server UI + DI composition root + Aspire ServiceDefaults. |
| `NewsAggregator.AppHost` | .NET Aspire app model (Web + Ollama container). |
| `NewsAggregator.Tests` | xUnit: dependency-rule guard, domain, application-service (fakes), `FakeChatClient`. |

Package versions are centrally pinned in [`Directory.Packages.props`](Directory.Packages.props)
(verified against nuget.org).

## Build & test

```bash
dotnet build NewsAggregator.slnx
dotnet test  NewsAggregator.Tests/NewsAggregator.Tests.csproj
```

## Run locally (Aspire)

`dotnet run` on the AppHost starts the Web app and an Ollama container with the
Aspire dashboard. Requires Docker and a .NET 10 SDK with Aspire support.

```bash
dotnet run --project NewsAggregator.AppHost
```

To run the Web app on its own (point `Models:Ollama:Endpoint` at a host Ollama):

```bash
dotnet run --project NewsAggregator.Web
```

## Model providers

- **Ollama (local, default)** — `OllamaSharp.OllamaApiClient` as `IChatClient`.
- **OpenRouter (BYOK)** — OpenAI-compatible; set `Models:Provider` to `OpenRouter`
  and supply the key (never commit it):

  ```bash
  dotnet user-secrets --project NewsAggregator.Web \
    set "Models:OpenRouter:ApiKey" "sk-or-..."
  ```
