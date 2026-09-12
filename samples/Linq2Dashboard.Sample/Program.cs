using Linq2Dashboard;
using Linq2Dashboard.Sample.Components;
using Linq2Dashboard.Sample.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// One dashboard for the whole application: immutable, thread-safe, built once at startup (concept §4.9).
builder.Services.AddSingleton<Dashboard<SampleOrder>>(_ => SampleData.BuildDashboard(rows: 200_000));

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
