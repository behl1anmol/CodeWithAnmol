using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NewsAggregator.Core.Configuration;
using NewsAggregator.Core.Domain;
using NewsAggregator.Infrastructure.HealthChecks;
using NewsAggregator.Tests.Fakes;
using NSubstitute;
using Xunit;

namespace NewsAggregator.Tests.HealthChecks;

/// <summary>
/// Behaviour of the model-provider reachability probe against canned HTTP responses (never a
/// live provider): Ollama 200/error/transport-failure, OpenRouter reachability semantics, and
/// the security guarantee that the configured API key is never surfaced in the result.
/// </summary>
public sealed class ModelProviderHealthCheckTests
{
    private const string OllamaEndpoint = "http://localhost:11434";
    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1";
    private const string SentinelKey = "SENTINEL-KEY-do-not-leak";

    // ---- helpers ------------------------------------------------------------

    private static ModelProviderHealthCheck BuildSut(FakeHttpMessageHandler handler, ModelOptions options)
    {
        var client = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ModelProviderHealthCheck.HttpClientName).Returns(client);
        return new ModelProviderHealthCheck(factory, Options.Create(options));
    }

    private static ModelOptions OllamaOptions() => new()
    {
        Provider = ModelProvider.Ollama,
        Ollama = new OllamaOptions { Endpoint = OllamaEndpoint },
    };

    private static ModelOptions OpenRouterOptions() => new()
    {
        Provider = ModelProvider.OpenRouter,
        OpenRouter = new OpenRouterOptions { Endpoint = OpenRouterEndpoint, ApiKey = SentinelKey },
    };

    private static Task<HealthCheckResult> RunAsync(ModelProviderHealthCheck sut)
        => sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

    private static void AssertKeyNeverSurfaced(HealthCheckResult result)
    {
        Assert.DoesNotContain(SentinelKey, result.Description ?? string.Empty);
        Assert.DoesNotContain(SentinelKey, result.Exception?.ToString() ?? string.Empty);
    }

    // ---- Ollama -------------------------------------------------------------

    [Fact]
    public async Task Ollama_healthy_when_tags_endpoint_returns_200()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{\"models\":[]}"));
        ModelProviderHealthCheck sut = BuildSut(handler, OllamaOptions());

        HealthCheckResult result = await RunAsync(sut);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("/api/tags", handler.RequestedPaths);
    }

    [Fact]
    public async Task Ollama_unhealthy_when_endpoint_returns_error_status()
    {
        var handler = new FakeHttpMessageHandler(
            _ => FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError));
        ModelProviderHealthCheck sut = BuildSut(handler, OllamaOptions());

        HealthCheckResult result = await RunAsync(sut);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Ollama_unhealthy_when_transport_fails()
    {
        var handler = new FakeHttpMessageHandler(
            _ => throw new HttpRequestException("connection refused"));
        ModelProviderHealthCheck sut = BuildSut(handler, OllamaOptions());

        HealthCheckResult result = await RunAsync(sut);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Ollama_unhealthy_when_probe_times_out()
    {
        // HttpClient surfaces a bounded-timeout as TaskCanceledException with no caller cancel.
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("timeout"));
        ModelProviderHealthCheck sut = BuildSut(handler, OllamaOptions());

        HealthCheckResult result = await RunAsync(sut);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // ---- OpenRouter ---------------------------------------------------------

    [Fact]
    public async Task OpenRouter_healthy_when_endpoint_responds()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{\"data\":[]}"));
        ModelProviderHealthCheck sut = BuildSut(handler, OpenRouterOptions());

        HealthCheckResult result = await RunAsync(sut);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task OpenRouter_healthy_on_any_response_because_reachability_is_the_signal()
    {
        // 401 still proves the endpoint is routable; the probe never authenticates.
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Status(HttpStatusCode.Unauthorized));
        ModelProviderHealthCheck sut = BuildSut(handler, OpenRouterOptions());

        HealthCheckResult result = await RunAsync(sut);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task OpenRouter_unhealthy_when_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("dns failure"));
        ModelProviderHealthCheck sut = BuildSut(handler, OpenRouterOptions());

        HealthCheckResult result = await RunAsync(sut);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // ---- key safety ---------------------------------------------------------

    [Fact]
    public async Task Api_key_never_surfaced_when_reachable()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("{}"));
        ModelProviderHealthCheck sut = BuildSut(handler, OpenRouterOptions());

        AssertKeyNeverSurfaced(await RunAsync(sut));
    }

    [Fact]
    public async Task Api_key_never_surfaced_when_unreachable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        ModelProviderHealthCheck sut = BuildSut(handler, OpenRouterOptions());

        AssertKeyNeverSurfaced(await RunAsync(sut));
    }
}
