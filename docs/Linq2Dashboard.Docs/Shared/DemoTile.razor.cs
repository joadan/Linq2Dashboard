using Linq2Dashboard.Blazor;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Docs.Shared;

public partial class DemoTile
{
    /// <summary>The formatted pieces the metric component hands its template.</summary>
    [Parameter, EditorRequired]
    public MetricTileContent Content { get; set; } = default!;

    /// <summary>The demo icon to draw beside the text.</summary>
    [Parameter, EditorRequired]
    public string Icon { get; set; } = string.Empty;

    /// <summary>The metric key, which the stylesheet turns into the tile hue.</summary>
    private string Key => Content.Metric.Key;
}
