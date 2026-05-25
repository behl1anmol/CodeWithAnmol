using Microsoft.Extensions.Time.Testing;
using ShopApi.Discounts;
using Xunit;

namespace ShopApi.Tests;

public class CouponServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Coupon_is_redeemable_before_expiry()
    {
        var time = new FakeTimeProvider(Now);
        var service = new CouponService(time);
        var coupon = new Coupon { Code = "SUMMER25", ExpiresAt = Now.AddHours(1) };

        Assert.True(service.IsRedeemable(coupon));
    }

    [Fact]
    public void Coupon_is_not_redeemable_after_expiry()
    {
        var time = new FakeTimeProvider(Now);
        var service = new CouponService(time);
        var coupon = new Coupon { Code = "SUMMER25", ExpiresAt = Now.AddHours(1) };

        time.SetUtcNow(Now.AddHours(2));

        Assert.False(service.IsRedeemable(coupon));
    }
}
