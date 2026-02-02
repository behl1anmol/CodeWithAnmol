using NewsAggregator.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server UI. Application + Infrastructure services are wired into this
// composition root in a later episode, once the UI first needs them.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
