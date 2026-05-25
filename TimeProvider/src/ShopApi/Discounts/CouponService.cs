namespace ShopApi.Discounts;

public sealed class CouponService(TimeProvider timeProvider)
{
    public bool IsRedeemable(Coupon coupon) =>
        timeProvider.GetUtcNow() < coupon.ExpiresAt;
}
