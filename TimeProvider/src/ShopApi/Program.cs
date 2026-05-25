using ShopApi.Discounts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CouponService>();

var app = builder.Build();

app.MapGet("/", () => "TimeProvider Demo — run `dotnet test ../ShopApi.Tests` to see deterministic tests.");

app.MapGet("/coupon/{code}", (string code, CouponService svc) =>
{
    var coupon = new Coupon
    {
        Code = code,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
    };
    return svc.IsRedeemable(coupon)
        ? Results.Ok($"Coupon '{code}' is redeemable.")
        : Results.BadRequest($"Coupon '{code}' has expired.");
});

app.Run();
