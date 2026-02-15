using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Application.Services;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.DependencyInjection;
using NewsAggregator.Infrastructure.HealthChecks;
using NewsAggregator.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration (Options pattern + fail-fast validation) ---
builder.Services.AddOptions<SourceOptions>()
    .BindConfiguration(SourceOptions.SectionName)
    .ValidateOnStart();

builder.Services.AddOptions<ModelOptions>()
    .BindConfiguration(ModelOptions.SectionName)
    .Validate(
        options => options.Provider != ModelProvider.OpenRouter
            || !string.IsNullOrWhiteSpace(options.OpenRouter.ApiKey),
        "Models:OpenRouter:ApiKey is required when Models:Provider is 'OpenRouter'.")
    .ValidateOnStart();

builder.Services.AddOptions<EnrichmentOptions>()
    .BindConfiguration(EnrichmentOptions.SectionName)
    .ValidateOnStart();

builder.Services.AddOptions<CacheOptions>()
    .BindConfiguration(CacheOptions.SectionName)
    .ValidateOnStart();

// --- Core application services (framework-free; wired here in the composition root) ---
builder.Services.AddScoped<INewsAggregationService, NewsAggregationService>();
builder.Services.AddScoped<IDigestApplicationService, DigestApplicationService>();

// --- Infrastructure adapters bound to Core ports ---
builder.Services.AddInfrastructure();

// Probe the active model provider; surfaced on /health. "ready" marks it as a readiness check.
builder.Services.AddHealthChecks()
    .AddCheck<ModelProviderHealthCheck>("model-provider", tags: ["ready"]);

// --- Blazor Server UI ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
