using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// The header shared by every facet component: name, optional extra content, an × that clears the facet and a collapse toggle (design §9).
/// A rendering detail of the facet components, not part of the supported API: it is public only because Razor
/// components cannot be internal, is hidden from IntelliSense, and may change without notice.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class FacetHeader
{
    /// <summary>The facet's name.</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>Extra content between the name and the clear button, such as a range facet's bounds.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Show the clear button; the facets set it when they have a selection.</summary>
    [Parameter]
    public bool ShowClear { get; set; }

    /// <summary>Accessible label and tooltip of the clear button, which shows an ×.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Raised when the clear button is clicked.</summary>
    [Parameter]
    public EventCallback OnClear { get; set; }

    /// <summary>When set, the clear button is a link to this URL instead of raising <see cref="OnClear"/>: the view renders links (design §9).</summary>
    [Parameter]
    public string? ClearHref { get; set; }

    /// <summary>Render the name as a toggle that raises <see cref="OnToggle"/>.</summary>
    [Parameter]
    public bool Collapsible { get; set; }

    /// <summary>Whether the body is hidden; drives the toggle's state and the collapsed class.</summary>
    [Parameter]
    public bool Collapsed { get; set; }

    /// <summary>Raised when the collapsible name is clicked.</summary>
    [Parameter]
    public EventCallback OnToggle { get; set; }
}
