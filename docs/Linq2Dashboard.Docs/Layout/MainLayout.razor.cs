using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Linq2Dashboard.Docs.Layout;

/// <summary>The site shell. The demo page drops the reading-width limit so the dashboard can use the whole screen.</summary>
public partial class MainLayout : IDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private bool IsWide => Navigation.ToBaseRelativePath(Navigation.Uri).StartsWith("demo", StringComparison.OrdinalIgnoreCase);

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => StateHasChanged();
}
