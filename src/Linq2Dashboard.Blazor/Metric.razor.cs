using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// One metric tile, addressed by key like the facet components (design §9). The metric itself is
/// defined in the builder; this component only places and formats it.
/// </summary>
public partial class Metric<T>
{
    /// <summary>The metric key, as defined in the builder.</summary>
    [Parameter, EditorRequired]
    public string Key { get; set; } = default!;

    /// <summary>Overrides the title given in the builder.</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>
    /// Shows the metric's share of its total under the value, as a percentage (concept §4.4). Only
    /// count, sum and distinct metrics have a share; for the others nothing is added.
    /// </summary>
    [Parameter]
    public bool ShowShare { get; set; }

    /// <summary>An icon shown beside the title and value, typically an inline SVG or an icon-font element (design §9.5).</summary>
    [Parameter]
    public RenderFragment? Icon { get; set; }

    /// <summary>Replaces the title and value inside the tile. The tile element and its classes stay.</summary>
    [Parameter]
    public RenderFragment<MetricState>? MetricTemplate { get; set; }

    private MetricState Current => State.Metric(Key);

    private string? ShareText => ShowShare && Current.Share is double share ? Formatter.FormatShare(share) : null;

    private RenderFragment? Template => MetricTemplate is null ? null : MetricTemplate(Current);
}
