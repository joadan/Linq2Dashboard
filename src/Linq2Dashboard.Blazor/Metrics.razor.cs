using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

public partial class Metrics<T>
{
    /// <summary>Metric keys to show, in this order. Default: every metric in definition order.</summary>
    [Parameter]
    public IReadOnlyList<string>? Keys { get; set; }

    /// <summary>Replaces the title and value inside each tile. The tile element and its classes stay.</summary>
    [Parameter]
    public RenderFragment<MetricState>? MetricTemplate { get; set; }

    /// <summary>Prepend a tile with the number of matching rows (concept §3).</summary>
    [Parameter]
    public bool IncludeMatchingCount { get; set; }

    [Parameter]
    public string MatchingCountTitle { get; set; } = "Matching";

    private IEnumerable<MetricState> Shown =>
        Keys is null ? State.Metrics : Keys.Select(State.Metric);
}
