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

    /// <summary>Overrides the name given in the builder.</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>
    /// Shows the metric's share of its total under the value, as a percentage (concept §4.4). Only
    /// count, sum and distinct metrics have a share; for the others nothing is added.
    /// </summary>
    [Parameter]
    public bool ShowShare { get; set; }

    /// <summary>
    /// Replaces the name and value inside the tile with the template's markup. The tile element and its
    /// classes stay. The context carries the formatted pieces and the raw state (design §9.5).
    /// </summary>
    [Parameter]
    public RenderFragment<MetricTileContent>? MetricTemplate { get; set; }

    private MetricState Current => State.Metric(Key);

    private MetricTileContent Content
    {
        get
        {
            MetricState current = Current;
            string? share = ShowShare && current.Share is double value ? Formatter.FormatShare(value) : null;
            return new MetricTileContent(Name ?? current.Name, Formatter.FormatMetric(current), share, !current.HasValue, current);
        }
    }

    private RenderFragment? Template => MetricTemplate is null ? null : MetricTemplate(Content);
}
