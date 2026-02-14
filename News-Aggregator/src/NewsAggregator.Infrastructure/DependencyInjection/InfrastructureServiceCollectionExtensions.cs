using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Application.Ports;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.Agents;
using NewsAggregator.Infrastructure.Caching;
using NewsAggregator.Infrastructure.Models;
using NewsAggregator.Infrastructure.Sources;
using NewsAggregator.Infrastructure.Workflows;

namespace NewsAggregator.Infrastructure.DependencyInjection;

/// <summary>
/// Binds every Infrastructure adapter to its Core port. Called once from the Web
/// composition root. All dependencies are constructor-injected — no service
/// locator. Options are assumed already bound by the composition root.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSources();
        services.AddModelProviders();
        services.AddAgentsAndWorkflows();
        services.AddCaching();
        return services;
    }

    private static void AddSources(this IServiceCollection services)
    {
        services.AddHttpClient(HackerNewsSource.HttpClientName, client =>
            client.BaseAddress = new Uri("https://hacker-news.firebaseio.com/v0/"));
        services.AddHttpClient(RssNewsSource.HttpClientName);
        services.AddHttpClient(GitHubNewsSource.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            // GitHub rejects API requests without a User-Agent; the others pin the JSON
            // media type and API version so responses stay stable across server-side changes.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NewsAggregator");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });

        services.AddSingleton<INewsSource, HackerNewsSource>();
        services.AddSingleton<INewsSource, RssNewsSource>();
        services.AddSingleton<INewsSource, GitHubNewsSource>();
    }

    private static void AddModelProviders(this IServiceCollection services)
    {
        // The active provider is chosen from configuration at resolution time.
        services.AddSingleton<IChatModelProvider>(static sp =>
        {
            ModelOptions options = sp.GetRequiredService<IOptions<ModelOptions>>().Value;
            return options.Provider switch
            {
                ModelProvider.Ollama =>
                    new OllamaChatModelProvider(sp.GetRequiredService<IOptions<ModelOptions>>()),
                ModelProvider.OpenRouter =>
                    new OpenRouterChatModelProvider(sp.GetRequiredService<IOptions<ModelOptions>>()),
                _ => throw new InvalidOperationException(
                    $"Unsupported model provider '{options.Provider}'."),
            };
        });

        services.AddSingleton<IChatClientFactory, ChatClientFactory>();

        // The model-provider reachability probe (and its single-shot HttpClient) is added in a
        // later episode together with ModelProviderHealthCheck.
    }

    private static void AddAgentsAndWorkflows(this IServiceCollection services)
    {
        // The editorial workflow stamps Digest.GeneratedAt from an injected clock so the
        // timestamp is deterministic in tests; production uses the system clock.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAgentFactory, AgentFrameworkAgentFactory>();
        services.AddScoped<IEnrichmentWorkflow, ConcurrentEnrichmentWorkflow>();
        services.AddScoped<IEditorialWorkflow, SequentialEditorialWorkflow>();
    }

    private static void AddCaching(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<IDigestCache, InMemoryDigestCache>();
    }
}
