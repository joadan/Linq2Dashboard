using Linq2Dashboard.Sample;
using Linq2Dashboard.Sample.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The shared dashboard behind Home and Static comes from a cache service: built on the first request, shared by every
// user, rebuilt after it expires (concept §7). The /small page builds its own and needs nothing here.
builder.Services.AddHybridCache();
builder.Services.AddSingleton<OrdersDashboardService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
