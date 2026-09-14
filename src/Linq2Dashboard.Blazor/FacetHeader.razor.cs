using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// The header shared by every facet component: title, optional extra content, a clear link and a collapse toggle (design §9).
/// A rendering detail of the facet components, not part of the supported API: it is public only because Razor
/// components cannot be internal, is hidden from IntelliSense, and may change without notice.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class FacetHeader
{
    /// <summary>The facet's title.</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    /// <summary>Extra content between the title and the clear link, such as a range facet's bounds.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Show the clear link; the facets set it when they have a selection.</summary>
    [Parameter]
    public bool ShowClear { get; set; }

    /// <summary>Text of the clear link.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Raised when the clear link is clicked.</summary>
    [Parameter]
    public EventCallback OnClear { get; set; }

    /// <summary>Render the title as a toggle that raises <see cref="OnToggle"/>.</summary>
    [Parameter]
    public bool Collapsible { get; set; }

    /// <summary>Whether the body is hidden; drives the toggle's state and the collapsed class.</summary>
    [Parameter]
    public bool Collapsed { get; set; }

    /// <summary>Raised when the collapsible title is clicked.</summary>
    [Parameter]
    public EventCallback OnToggle { get; set; }
}
