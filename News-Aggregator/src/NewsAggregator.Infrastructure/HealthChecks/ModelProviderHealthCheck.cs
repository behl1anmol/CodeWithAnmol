using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;

namespace NewsAggregator.Infrastructure.HealthChecks;

/// <summary>
/// Pings the <em>active</em> model provider so the Aspire dashboard and <c>/health</c>
/// report whether the configured Ollama endpoint / OpenRouter API is reachable
/// (docs §5.3, §6.2). The probe is GET-only, bounded by a short client timeout, and
/// <strong>never</strong> sends or surfaces the OpenRouter API key.
/// </summary>
public sealed class ModelProviderHealthCheck : IHealthCheck
{
    /// <summary>
    /// Named <see cref="HttpClient"/> used for the reachability probe. Configured with a
    /// short timeout in the composition root so a down provider fails fast.
    /// </summary>
    public const string HttpClientName = "model-provider-health";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ModelOptions _options;

    public ModelProviderHealthCheck(IHttpClientFactory httpClientFactory, IOptions<ModelOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            return _options.Provider switch
            {
                ModelProvider.Ollama => await ProbeOllamaAsync(client, cancellationToken),
                ModelProvider.OpenRouter => await ProbeOpenRouterAsync(client, cancellationToken),
                _ => HealthCheckResult.Unhealthy($"Unsupported model provider '{_options.Provider}'."),
            };
        }
        catch (HttpRequestException ex)
        {
            // Connection refused / DNS failure / etc. — the provider is unreachable.
            // The message carries no secret (the key never goes on the request).
            return HealthCheckResult.Unhealthy(DescribeUnreachable(), ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The bounded client timeout elapsed (not a caller cancellation) → unreachable.
            return HealthCheckResult.Unhealthy(DescribeUnreachable(), ex);
        }
    }

    private async Task<HealthCheckResult> ProbeOllamaAsync(HttpClient client, CancellationToken ct)
    {
        // /api/tags is Ollama's cheap "list local models" endpoint: a 200 proves the
        // runtime is up and its HTTP API answers, without pulling or running any model.
        string endpoint = _options.Ollama.Endpoint;
        var probe = new Uri($"{endpoint.TrimEnd('/')}/api/tags");

        using HttpResponseMessage response = await client.GetAsync(probe, ct);
        return response.IsSuccessStatusCode
            ? HealthCheckResult.Healthy($"Ollama reachable at {endpoint}.")
            : HealthCheckResult.Unhealthy(
                $"Ollama at '{endpoint}' returned {(int)response.StatusCode} ({response.StatusCode}).");
    }

    private async Task<HealthCheckResult> ProbeOpenRouterAsync(HttpClient client, CancellationToken ct)
    {
        // Reachability only: any HTTP response (even 401/404) proves the endpoint is
        // routable. We send no Authorization header, so the API key never leaves config
        // and a real completion is never requested.
        string endpoint = _options.OpenRouter.Endpoint;

        using HttpResponseMessage response = await client.GetAsync(endpoint, ct);
        return HealthCheckResult.Healthy($"OpenRouter endpoint reachable at {endpoint}.");
    }

    private string DescribeUnreachable() => _options.Provider switch
    {
        ModelProvider.Ollama => $"Ollama endpoint '{_options.Ollama.Endpoint}' is unreachable.",
        ModelProvider.OpenRouter => $"OpenRouter endpoint '{_options.OpenRouter.Endpoint}' is unreachable.",
        _ => $"Model provider '{_options.Provider}' is unreachable.",
    };
}
