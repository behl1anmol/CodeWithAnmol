# 8. Local Secrets & Aspire Config Wiring

[← Aspire Topology & Docker](06-aspire-topology-and-docker.md) · [Index](README.md)

**Goal:** explain how configuration/secrets actually reach `NewsAggregator.Web` when
launched via `NewsAggregator.AppHost`, why `dotnet user-secrets` placement matters, and
the current Ollama-model mismatch bug this surfaced.

## 8.1 Two launch paths, same config system

```
dotnet run --project NewsAggregator.AppHost   # starts AppHost + Ollama + Web (Aspire-composed)
dotnet run --project NewsAggregator.Web       # starts Web alone, no AppHost
```

Both end up building the same `WebApplication` (`Program.cs`), so both go through the
same **.NET configuration provider chain**, in increasing precedence:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. **User Secrets** (Development only, loaded by `UserSecretsId` in the *Web* `.csproj`)
4. **Environment variables**
5. Command-line args

Higher number wins on key collision.

## 8.2 Why this matters for AppHost

When you run via AppHost, AppHost is a **separate process** that launches Web as a
child process and can inject environment variables into it — see `AppHost.cs`:

```csharp
builder.AddProject<Projects.NewsAggregator_Web>("webfrontend")
    .WithEnvironment("Models__Ollama__Endpoint", ollama.Resource.PrimaryEndpoint)
    .WaitFor(model)
    .WithExternalHttpEndpoints();
```

`WithEnvironment(key, value)` sets an OS environment variable on the **child Web
process**. Config keys with `:` become `__` (double underscore) as env var names —
`Models:Ollama:Endpoint` → `Models__Ollama__Endpoint`. This is why the Ollama endpoint
works automatically: AppHost discovers the container's real port and injects it — you
never hardcode `localhost:11434` for the AppHost path.

Because env vars are **above** user secrets in the precedence chain (§8.1), anything
AppHost injects this way overrides whatever you set with `dotnet user-secrets` on Web.

## 8.3 Where `dotnet user-secrets` actually writes to

`dotnet user-secrets set` does **not** touch any project file. It writes to a JSON file
keyed by the project's `<UserSecretsId>` (Web's is `newsaggregator-web-9c1f0b1a`):

```
~/.microsoft/usersecrets/newsaggregator-web-9c1f0b1a/secrets.json
```

This file is loaded **by the Web process itself** at startup (`WebApplication.CreateBuilder`
auto-loads user secrets when `ASPNETCORE_ENVIRONMENT=Development`), regardless of who
launched that process. So:

- `dotnet user-secrets set "Models:OpenRouter:ApiKey" "sk-or-..."` run from
  `NewsAggregator.Web/` works **whether you launch via AppHost or directly** — no AppHost
  wiring required for this specific key, because nothing else (AppHost or appsettings)
  is currently setting `Models:OpenRouter:ApiKey` to compete with it.
- It does **not** require `dotnet user-secrets init` in `NewsAggregator.AppHost/` — that
  is a separate `UserSecretsId` scope, only relevant if you want AppHost itself to own
  the secret and hand it down via an Aspire **parameter** (see §8.5).

Verify what's stored:
```bash
cd NewsAggregator.Web
dotnet user-secrets list
```

## 8.4 What is actually a secret here

Checked every key bound by `Program.cs`'s `Options` registrations
(`SourceOptions`, `ModelOptions`, `EnrichmentOptions`, `CacheOptions`):

| Setting | Secret? | Why |
| --- | --- | --- |
| `Models:OpenRouter:ApiKey` | **Yes** | Grants spend against your OpenRouter account. |
| `Models:Provider` | No | Just a switch (`Ollama` / `OpenRouter`), no credential. |
| `Sources:*` (feeds, repos, limits) | No | Public URLs/config, has working defaults. |
| `Models:Ollama:Endpoint` | No | Local network address, injected by AppHost anyway. |

Only `Models:OpenRouter:ApiKey` belongs in a secret store. Everything else is fine in
`appsettings.json`.

## 8.5 Two valid patterns for the OpenRouter key

**Pattern A — Web owns it (current, simplest):**
```bash
cd NewsAggregator.Web
dotnet user-secrets set "Models:OpenRouter:ApiKey" "sk-or-v1-..."
dotnet user-secrets set "Models:Provider" "OpenRouter"
```
Works for both launch paths per §8.3. No AppHost changes needed.

**Pattern B — AppHost owns it as an Aspire secret parameter (more idiomatic for the
AppHost launch path, lets the dashboard show it's a managed parameter):**
```csharp
// AppHost.cs
var openRouterKey = builder.AddParameter("openrouter-apikey", secret: true);

builder.AddProject<Projects.NewsAggregator_Web>("webfrontend")
    .WithEnvironment("Models__OpenRouter__ApiKey", openRouterKey)
    .WithEnvironment("Models__Provider", "OpenRouter")
    .WithEnvironment("Models__Ollama__Endpoint", ollama.Resource.PrimaryEndpoint)
    .WaitFor(model)
    .WithExternalHttpEndpoints();
```
```bash
cd NewsAggregator.AppHost
dotnet user-secrets init
dotnet user-secrets set "Parameters:openrouter-apikey" "sk-or-v1-..."
```
Only works through the AppHost launch path (the parameter is resolved by AppHost, then
pushed down as an env var — same mechanism as §8.2).

**Never:** hardcoding the key literal in `appsettings.json` or `appsettings.Development.json`
— both are committed to git. (See §8.7 — this currently exists in the repo and needs
rotation.)

## 8.6 Why Ollama pulls `llama3.2` regardless of `appsettings.json`

`AppHost.cs` declares the Ollama resource and the model to pull **as a hardcoded
literal**, independent of any `Models:Ollama:DefaultModel` config value:

```csharp
var ollama = builder.AddOllama("ollama").WithDataVolume();
var model = ollama.AddModel("llama3.2");   // <-- pulled at AppHost startup, always
```

This line controls **what container image/model AppHost pulls and waits on** before
starting Web (`.WaitFor(model)`). It has nothing to do with `appsettings.json` — that
file's `Models:Ollama:DefaultModel` only controls **what model id the Web app asks the
already-running Ollama server for** at chat time.

These two are independent strings that must be kept in sync **by hand** — the code
comment at `AppHost.cs` says so explicitly:

> *"The model name MUST match Web/appsettings.json `Models:Ollama:DefaultModel`."*

If they drift (as they currently have — AppHost pulls `llama3.2`, `appsettings.json`
requests `gemma4:e2b`), AppHost happily starts (it only validates that the model it
itself was told to pull succeeds), but the **first chat call from Web will fail** because
Ollama was never asked to pull `gemma4:e2b`.

## 8.7 Action items found while writing this guide

- [ ] `NewsAggregator.Web/appsettings.json` line 45 still has a **live OpenRouter key
      committed in plaintext**. Rotate the key at openrouter.ai, then blank/remove that
      line and use §8.5 Pattern A or B instead.
- [ ] `AppHost.cs`'s `AddModel("llama3.2")` and `appsettings.json`'s
      `Models:Ollama:DefaultModel` (`gemma4:e2b`) have drifted — pick one model and align
      both. Also confirm `gemma4:e2b` is a real Ollama tag (Gemma's released generations
      are 2 and 3, not 4) — likely meant `gemma3:2b` or `gemma2:2b`.

[← Aspire Topology & Docker](06-aspire-topology-and-docker.md) · [Index](README.md)
