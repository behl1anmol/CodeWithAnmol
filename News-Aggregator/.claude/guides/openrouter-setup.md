# Guide — Wiring the app to OpenRouter (BYOK)

Related spec: [docs §4 Model Providers & BYOK](../../docs/04-model-providers-and-byok.md) ·
[docs §6.1 Aspire topology](../../docs/06-aspire-topology-and-docker.md)

This is an operational how-to for pointing News-Aggregator at **OpenRouter** instead of the
default local **Ollama**. OpenRouter is OpenAI-compatible and "bring your own key" (BYOK).

Switching providers is **configuration-only — no code change**. The provider is selected at
runtime from the `Models` configuration section; the composition root resolves the matching
provider + chat client (`InfrastructureServiceCollectionExtensions.AddModelProviders` →
`ChatClientFactory`).

---

## TL;DR

1. Set `Models:Provider` to `OpenRouter`.
2. Supply `Models:OpenRouter:ApiKey` **as a secret** (user-secrets / env var / Aspire parameter — **never commit it**).
3. (Optional) pick a model with `Models:OpenRouter:DefaultModel`.
4. Run; check `/health` shows `model-provider` healthy.

```bash
# Dev, running the Web app directly:
dotnet user-secrets set "Models:Provider"             "OpenRouter"          --project src/NewsAggregator.Web
dotnet user-secrets set "Models:OpenRouter:ApiKey"    "sk-or-v1-XXXXXXXX"   --project src/NewsAggregator.Web
dotnet user-secrets set "Models:OpenRouter:DefaultModel" "openai/gpt-4o-mini" --project src/NewsAggregator.Web

export PATH=$HOME/.dotnet:$PATH
dotnet run --project src/NewsAggregator.Web
```

> The Web project already has a `UserSecretsId` (`newsaggregator-web-9c1f0b1a`), so user-secrets
> works without any extra init.

---

## 1. The `Models` configuration section

Bound to `ModelOptions` (`src/NewsAggregator.Core/Configuration/ModelOptions.cs`), section name
`Models`. Defaults shipped in `src/NewsAggregator.Web/appsettings.json`:

```jsonc
{
  "Models": {
    "Provider": "Ollama",                          // <-- change to "OpenRouter"
    "OpenRouter": {
      "Endpoint": "https://openrouter.ai/api/v1",  // OpenAI-compatible base URL
      "DefaultModel": "openai/gpt-4o-mini",         // any OpenRouter model id
      "ApiKey": null                                // SECRET — do NOT put it here
    },
    "AgentModels": {                                // optional per-agent overrides
      "Summarizer": null,
      "Categorizer": null,
      "Ranker": null,
      "Editor": null
    }
  }
}
```

| Key | Meaning | Default |
|---|---|---|
| `Models:Provider` | `Ollama` or `OpenRouter`. Selects the active provider at startup. | `Ollama` |
| `Models:OpenRouter:Endpoint` | OpenAI-compatible base URL. | `https://openrouter.ai/api/v1` |
| `Models:OpenRouter:DefaultModel` | Model id used for every agent unless overridden. | `openai/gpt-4o-mini` |
| `Models:OpenRouter:ApiKey` | **BYOK secret.** Required when provider is OpenRouter. | `null` |
| `Models:AgentModels:{Role}` | Override model id for one agent (`Summarizer`/`Categorizer`/`Ranker`/`Editor`); `null` ⇒ use `DefaultModel`. | `null` |

Set the non-secret values in `appsettings.json` (or an environment-specific
`appsettings.{Environment}.json`). **Keep `ApiKey` out of every committed file** — supply it via
one of the secret mechanisms in §2.

---

## 2. Supplying the API key (never commit it)

Pick whichever fits where you run the app. All three set the same config key
`Models:OpenRouter:ApiKey`.

### a) User-secrets — local dev (recommended)
Stored outside the repo, in your user profile:

```bash
dotnet user-secrets set "Models:OpenRouter:ApiKey" "sk-or-v1-XXXXXXXX" --project src/NewsAggregator.Web
# list / remove:
dotnet user-secrets list   --project src/NewsAggregator.Web
dotnet user-secrets remove "Models:OpenRouter:ApiKey" --project src/NewsAggregator.Web
```

### b) Environment variable — containers / CI
.NET maps `:` to `__` (double underscore) in env var names:

```bash
export Models__Provider="OpenRouter"
export Models__OpenRouter__ApiKey="sk-or-v1-XXXXXXXX"
dotnet run --project src/NewsAggregator.Web
```

### c) Aspire parameter — running via the AppHost
When you start everything with `dotnet run` on the AppHost, pass the key as a **secret Aspire
parameter** and inject it into the Web app as the env var. Edit
`src/NewsAggregator.AppHost/AppHost.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Secret parameter — value supplied via user-secrets / env, never committed.
var openRouterKey = builder.AddParameter("openrouter-apikey", secret: true);

builder.AddProject<Projects.NewsAggregator_Web>("webfrontend")
    .WithEnvironment("Models__Provider", "OpenRouter")
    .WithEnvironment("Models__OpenRouter__ApiKey", openRouterKey)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

Supply the parameter value (AppHost has its own `UserSecretsId`):

```bash
dotnet user-secrets set "Parameters:openrouter-apikey" "sk-or-v1-XXXXXXXX" --project src/NewsAggregator.AppHost
```

> **When using OpenRouter you do not need the Ollama container** that the AppHost wires for the
> local-model path (`AddOllama(...).AddModel(...)` + `WaitFor(model)`). Drop or keep it as you
> like — the Web app talks to whatever `Models:Provider` resolves to. If you keep both, only the
> selected provider is actually called.

---

## 3. What happens at startup (validation)

The composition root fails fast on a misconfigured OpenRouter setup
(`src/NewsAggregator.Web/Program.cs`):

```csharp
builder.Services.AddOptions<ModelOptions>()
    .BindConfiguration(ModelOptions.SectionName)
    .Validate(
        options => options.Provider != ModelProvider.OpenRouter
            || !string.IsNullOrWhiteSpace(options.OpenRouter.ApiKey),
        "Models:OpenRouter:ApiKey is required when Models:Provider is 'OpenRouter'.")
    .ValidateOnStart();
```

So if you set `Provider=OpenRouter` but forget the key, the app **refuses to start** with that
exact message — by design, not a crash mid-request.

---

## 4. How it's wired internally (for maintainers)

Configuration-only switching is possible because the provider is chosen at resolution time and
every provider goes through one factory:

| Step | File | Behaviour |
|---|---|---|
| Pick provider | `Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs` (`AddModelProviders`) | Reads `ModelOptions.Provider`; registers `OpenRouterChatModelProvider` (or `OllamaChatModelProvider`). |
| Describe model per role | `Infrastructure/Models/OpenRouterChatModelProvider.cs` | Returns a `ChatModelDescriptor` (provider, model id, endpoint). Applies the per-agent `AgentModels` override, else `DefaultModel`. |
| Build the chat client | `Infrastructure/Models/ChatClientFactory.cs` (`CreateOpenRouter`) | `new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint })` → `.GetChatClient(modelId).AsIChatClient()`. |
| Cross-cutting pipeline | same file | `.AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build(sp)` — applied uniformly to every provider. |

The OpenRouter call uses the official **`OpenAI`** SDK pointed at the OpenRouter base URL (verified
against `OpenAI` 2.10.0). The API key is read only inside `ChatClientFactory` from `ModelOptions`;
it never enters Core and is never logged.

> **Endpoint-override note:** `OpenAIClientOptions.Endpoint` is the override point. If a future
> `OpenAI` SDK version changes that surface, the documented fallback is a custom `HttpClient`
> transport whose `BaseAddress` points at OpenRouter (see [docs §4.4](../../docs/04-model-providers-and-byok.md)).

### Optional: OpenRouter attribution headers
OpenRouter accepts optional `HTTP-Referer` and `X-Title` ranking headers. They are **not required**
to function. To add them, supply a custom `HttpClient`/transport to the `OpenAIClient` with those
default headers (this is also the fallback transport mentioned above).

---

## 5. Verify it works

1. **Health check.** The active provider is probed at `/health` (and shown on the Aspire
   dashboard). `ModelProviderHealthCheck` sends a keyless `GET` to the OpenRouter base URL:
   - any response `< 500` (including `401`/`404`) ⇒ **Healthy** (endpoint is routable);
   - `>= 500` or a transport failure/timeout ⇒ **Unhealthy**.
   The API key is **never** sent on this probe, so the result text carries no secret.

   ```bash
   curl -s http://localhost:5xxx/health
   ```

2. **Generate a digest.** Open the app, hit **Refresh digest**. With a valid key + model you get a
   live-progress run then a categorized, ranked digest. With a bad key the agents fail and the UI
   shows a friendly error (the P4 error path), not a blank page.

---

## 6. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| App won't start: `Models:OpenRouter:ApiKey is required when Models:Provider is 'OpenRouter'.` | `Provider=OpenRouter` but no key found in any config source. | Set the key via §2 (user-secrets / env / Aspire parameter). |
| `InvalidOperationException: Models:OpenRouter:ApiKey is required when the provider is OpenRouter.` at first model call | Key missing/blank but startup validation was bypassed (e.g. provider flipped after start, or options not validated). | Ensure the key is present before the provider is used. |
| `/health` `model-provider` **Unhealthy**, "endpoint is unreachable" | No network to `openrouter.ai`, or a wrong `Endpoint`. | Check connectivity and `Models:OpenRouter:Endpoint`. |
| `401 Unauthorized` when generating a digest | Invalid/expired key, or no credit on the account. | Rotate the key; verify the OpenRouter account. (Health stays "Healthy" — it's keyless and only checks reachability.) |
| Model errors / empty output | `DefaultModel` (or an `AgentModels` override) is not a valid OpenRouter model id, or the model lacks the needed capability. | Use a valid id, e.g. `openai/gpt-4o-mini`. |

---

## Switching back to Ollama

Set `Models:Provider` back to `Ollama` (or unset the OpenRouter env vars). No code change. For the
local path, ensure a model is pulled — the AppHost does this via the Ollama hosting integration
(see [docs §6.1](../../docs/06-aspire-topology-and-docker.md)).
