using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

public partial class FacetHeader
{
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    /// <summary>Extra content between the title and the clear link, such as a range facet's bounds.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool ShowClear { get; set; }

    [Parameter]
    public string ClearText { get; set; } = "Clear";

    [Parameter]
    public EventCallback OnClear { get; set; }

    /// <summary>Render the title as a toggle that raises <see cref="OnToggle"/>.</summary>
    [Parameter]
    public bool Collapsible { get; set; }

    [Parameter]
    public bool Collapsed { get; set; }

    [Parameter]
    public EventCallback OnToggle { get; set; }
}
