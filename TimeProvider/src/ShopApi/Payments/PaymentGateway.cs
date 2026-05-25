namespace ShopApi.Payments;

public sealed class PaymentGateway(TimeProvider timeProvider)
{
    public async Task<string> ChargeAsync(Task<string> upstreamCall)
    {
        return await upstreamCall.WaitAsync(TimeSpan.FromSeconds(30), timeProvider);
    }
}
