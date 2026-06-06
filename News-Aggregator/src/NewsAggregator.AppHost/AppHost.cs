// Aspire app model — the composition root for INFRASTRUCTURE (not business logic).
// `dotnet run` here starts the Web app + Ollama (+ dashboard / telemetry / health).

var builder = DistributedApplication.CreateBuilder(args);

// Local LLM runtime via the CommunityToolkit Ollama HOSTING integration (docs §6.1).
// AddModel pulls the model on startup so a single `dotnet run` can serve a digest on the
// first run (the generic-container approach left the model un-pulled). The data volume
// persists pulled models across runs. The chat CLIENT is still OllamaSharp inside
// Infrastructure — only the hosting integration comes from the toolkit.
// Verified against CommunityToolkit.Aspire.Hosting.Ollama 13.4.0:
//   AddOllama(name) -> IResourceBuilder<OllamaResource>; WithDataVolume(); AddModel(modelName).
var ollama = builder.AddOllama("ollama")
    .WithDataVolume();

// The model name MUST match Web/appsettings.json `Models:Ollama:DefaultModel`. The AppHost is
// infra composition only, so this is a documented literal rather than shared config.
var model = ollama.AddModel("llama3.2");

builder.AddProject<Projects.NewsAggregator_Web>("webfrontend")
    // Inject the Ollama server endpoint into the Web app's configuration. PrimaryEndpoint is the
    // verified accessor (CommunityToolkit OllamaResource); the env var name/shape is unchanged so
    // the Infrastructure provider binding stays the same.
    .WithEnvironment("Models__Ollama__Endpoint", ollama.Resource.PrimaryEndpoint)
    // Wait for the MODEL resource (pull complete + healthy), not just the server, so the first
    // refresh has a model to serve.
    .WaitFor(model)
    .WithExternalHttpEndpoints();

// TODO(post-MVP / Lean MVP defers this): optional Redis distributed cache.
//   var redis = builder.AddRedis("redis");
//   ...AddProject(...).WithReference(redis);

builder.Build().Run();
