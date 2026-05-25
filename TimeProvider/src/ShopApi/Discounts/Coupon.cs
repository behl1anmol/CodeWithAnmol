namespace ShopApi.Discounts;

public sealed class Coupon
{
    public required string Code { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
