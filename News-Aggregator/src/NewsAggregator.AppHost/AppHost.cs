// Aspire app model — the composition root for INFRASTRUCTURE (not business logic).
// The Web app, Ollama bootstrap, telemetry and health are wired here in a later episode.

var builder = DistributedApplication.CreateBuilder(args);

builder.Build().Run();
