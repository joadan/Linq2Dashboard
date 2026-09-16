using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// One metric tile, addressed by key like the facet components (design §9). The metric itself is
/// defined in the builder; this component only places and formats it. The default tile shows the
/// name, the value and, for count, sum and distinct, the share of the total as a percentage (concept §4.4).
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
    /// Replaces the whole tile with the template's markup: the library renders no element of its own, so the
    /// template owns the root and puts its own classes and hooks on it. <c>Class</c> and extra attributes apply
    /// to the default tile only. The context carries the formatted pieces, the share included whenever the metric
    /// has one, and the raw state (design §9.5).
    /// </summary>
    [Parameter]
    public RenderFragment<MetricTileContent>? MetricTemplate { get; set; }

    private MetricState Current => State.Metric(Key);

    private MetricTileContent Content
    {
        get
        {
            MetricState current = Current;
            string? share = current.Share is double value ? Formatter.FormatShare(value) : null;
            return new MetricTileContent(Name ?? current.Name, Formatter.FormatMetric(current), share, !current.HasValue, current);
        }
    }
}
