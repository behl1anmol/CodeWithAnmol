using System.Net;

namespace NewsAggregator.Tests.Fakes;

/// <summary>
/// Deterministic <see cref="HttpMessageHandler"/> test double. Routes each request to a
/// caller-supplied responder (keyed off the request in the test), records the paths it was
/// asked for, and honours cancellation — so <c>HackerNewsSource</c> can be exercised
/// against canned JSON without ever touching the live Hacker News API.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Absolute paths of every request received, in order.</summary>
    public List<string> RequestedPaths { get; } = [];

    /// <summary>Builds a 200 OK response carrying a JSON body.</summary>
    public static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    /// <summary>Builds a response with an arbitrary (typically error) status code.</summary>
    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestedPaths.Add(request.RequestUri!.AbsolutePath);
        return Task.FromResult(_responder(request));
    }
}
