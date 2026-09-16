using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// The default tile rendered by <see cref="Metric{T}"/> and <see cref="MatchingCount{T}"/> when they have no
/// template, so both look the same and are styled once.
/// A rendering detail of those two components, not part of the supported API: it is public only because Razor
/// components cannot be internal, is hidden from IntelliSense, and may change without notice (design §9).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class MetricTile
{
    /// <summary>The metric key, written as <c>data-key</c> for styling and tests. Empty for the matching count.</summary>
    [Parameter]
    public string Key { get; set; } = string.Empty;

    /// <summary>The tile's name.</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>The formatted value.</summary>
    [Parameter, EditorRequired]
    public string Value { get; set; } = string.Empty;

    /// <summary>The formatted share of the total, shown under the value; null renders nothing (concept §4.4).</summary>
    [Parameter]
    public string? Share { get; set; }

    /// <summary>Marks a tile whose metric has no value (concept §4.4).</summary>
    [Parameter]
    public bool IsEmpty { get; set; }

    /// <summary>Extra classes after the tile's own, from the placing component's <c>Class</c> (design §9).</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Attributes the placing component passes through onto the tile element (design §9).</summary>
    [Parameter]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string RootClass
    {
        get
        {
            string classes = "l2d-metric";
            if (IsEmpty)
            {
                classes += " l2d-metric-empty";
            }

            return string.IsNullOrWhiteSpace(Class) ? classes : $"{classes} {Class.Trim()}";
        }
    }
}
