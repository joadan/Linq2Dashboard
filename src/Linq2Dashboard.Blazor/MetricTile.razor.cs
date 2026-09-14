using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>The tile rendered by <see cref="Metric{T}"/> and <see cref="MatchingCount{T}"/>, so both look the same and are styled once.</summary>
public partial class MetricTile
{
    /// <summary>The metric key, written as <c>data-key</c> for styling and tests. Empty for the matching count.</summary>
    [Parameter]
    public string Key { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    /// <summary>The formatted value.</summary>
    [Parameter, EditorRequired]
    public string Value { get; set; } = string.Empty;

    /// <summary>The formatted share of the total, shown under the value; null renders nothing (concept §4.4).</summary>
    [Parameter]
    public string? Share { get; set; }

    /// <summary>Marks a tile whose metric has no value (concept §4.4).</summary>
    [Parameter]
    public bool IsEmpty { get; set; }

    [Parameter]
    public string? CssClass { get; set; }

    /// <summary>
    /// An icon shown beside the title and value, typically an inline SVG or an icon-font element (design §9.5).
    /// Null renders no icon slot. Decorative: it is hidden from assistive technology.
    /// </summary>
    [Parameter]
    public RenderFragment? Icon { get; set; }

    /// <summary>Replaces the title and value; the tile element and its classes stay.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
