using Microsoft.Extensions.AI;
using Xunit;

namespace NewsAggregator.Tests.Fakes;

public sealed class FakeChatClientTests
{
    [Fact]
    public async Task Returns_the_canned_reply()
    {
        IChatClient client = new FakeChatClient("hello");

        ChatResponse response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("hello", response.Text);
    }
}
