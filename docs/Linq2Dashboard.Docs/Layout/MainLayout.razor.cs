using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Linq2Dashboard.Docs.Layout;

/// <summary>How the shell arranges a page.</summary>
public enum LayoutMode
{
    /// <summary>The home page: full width, no sidebar and no table of contents; the sidebar opens from the menu button.</summary>
    Landing,

    /// <summary>The demo: full width for the dashboard, the sidebar behind the menu button.</summary>
    Wide,

    /// <summary>A documentation page: sidebar, reading column, table of contents, previous and next.</summary>
    Doc,
}

/// <summary>
/// The site shell: a sticky header, the sidebar from <see cref="DocsNavigation"/>, the page and its table of contents.
/// The home page and the demo drop the sidebar and the reading width so they can use the whole screen.
/// </summary>
public partial class MainLayout : IDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private bool menuOpen;

    private string Path
    {
        get
        {
            string relative = Navigation.ToBaseRelativePath(Navigation.Uri);
            int end = relative.IndexOfAny(['?', '#']);
            return (end < 0 ? relative : relative[..end]).TrimEnd('/');
        }
    }

    private LayoutMode Mode => Path switch
    {
        "" => LayoutMode.Landing,
        var path when path.Equals("demo", StringComparison.OrdinalIgnoreCase) => LayoutMode.Wide,
        _ => LayoutMode.Doc,
    };

    private DocsPage? Previous => DocsNavigation.Previous(DocsNavigation.IndexOf(Path));

    private DocsPage? Next => DocsNavigation.Next(DocsNavigation.IndexOf(Path));

    /// <inheritdoc />
    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    /// <inheritdoc />
    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        menuOpen = false;
        StateHasChanged();
    }

    private void ToggleMenu() => menuOpen = !menuOpen;

    private void CloseMenu() => menuOpen = false;
}
