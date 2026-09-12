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

    /// <summary>Replaces the title and value inside the tile. The tile element and its classes stay.</summary>
    [Parameter]
    public RenderFragment<MetricState>? MetricTemplate { get; set; }

    private MetricState Current => State.Metric(Key);

    private RenderFragment? Template => MetricTemplate is null ? null : MetricTemplate(Current);
}
