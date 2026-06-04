// Aspire app model — the composition root for INFRASTRUCTURE (not business logic).
// `dotnet run` here starts the Web app + Ollama (+ dashboard / telemetry / health).

var builder = DistributedApplication.CreateBuilder(args);

// Local LLM runtime modeled as a first-party generic container (docs §6.1).
// No Community Toolkit; the client is OllamaSharp inside Infrastructure.
var ollama = builder.AddContainer("ollama", "ollama/ollama")
    .WithHttpEndpoint(targetPort: 11434, name: "http")
    .WithVolume("ollama-models", "/root/.ollama"); // persist pulled models across runs

builder.AddProject<Projects.NewsAggregator_Web>("webfrontend")
    // Inject the container's endpoint into the Web app's configuration.
    .WithEnvironment("Models__Ollama__Endpoint", ollama.GetEndpoint("http"))
    .WaitFor(ollama)
    .WithExternalHttpEndpoints();

// TODO(post-MVP / Lean MVP defers this): optional Redis distributed cache.
//   var redis = builder.AddRedis("redis");
//   ...AddProject(...).WithReference(redis);

builder.Build().Run();
