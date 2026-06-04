using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace NewsAggregator.Tests.Fakes;

/// <summary>
/// Deterministic <see cref="IChatClient"/> test double so agent/workflow logic
/// can be unit-tested later without a live model (Ollama/OpenRouter).
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly string _cannedReply;

    public FakeChatClient(string cannedReply = "ok")
    {
        _cannedReply = cannedReply;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _cannedReply)));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, _cannedReply);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
