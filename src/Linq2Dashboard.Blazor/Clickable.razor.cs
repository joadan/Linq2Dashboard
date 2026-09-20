using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// The element behind every click in the library (design §9). With <see cref="Href"/> it is a link, which is what a
/// view that is not interactive renders so the click works as a navigation; without one it is a button with a click
/// handler. Both carry the same class, so the stylesheets do not know the difference. A rendering detail of the
/// components, not part of the supported API: public only because Razor components cannot be internal, hidden from
/// IntelliSense, and free to change without notice.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class Clickable
{
    /// <summary>The element's classes.</summary>
    [Parameter, EditorRequired]
    public string Class { get; set; } = string.Empty;

    /// <summary>The link target; when set the element is a link and <see cref="OnClick"/> is not rendered.</summary>
    [Parameter]
    public string? Href { get; set; }

    /// <summary>The click handler of the button form.</summary>
    [Parameter]
    public EventCallback OnClick { get; set; }

    /// <summary>The tooltip.</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>The accessible name, for an element whose content is a glyph.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>Whether the element is a toggle that is currently on. Rendered as <c>aria-pressed</c> on the button form; a link has no pressed state.</summary>
    [Parameter]
    public bool? Pressed { get; set; }

    /// <summary>The content.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private string? PressedAttribute => Pressed switch { true => "true", false => "false", null => null };
}
