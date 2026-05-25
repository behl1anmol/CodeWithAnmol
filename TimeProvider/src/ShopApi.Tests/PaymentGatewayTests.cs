using Microsoft.Extensions.Time.Testing;
using ShopApi.Payments;
using Xunit;

namespace ShopApi.Tests;

public class PaymentGatewayTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Charge_times_out_after_30_seconds()
    {
        var time = new FakeTimeProvider(Now);
        var gateway = new PaymentGateway(time);

        var tcs = new TaskCompletionSource<string>();
        var charge = gateway.ChargeAsync(tcs.Task);

        time.Advance(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<TimeoutException>(() => charge);
    }
}
