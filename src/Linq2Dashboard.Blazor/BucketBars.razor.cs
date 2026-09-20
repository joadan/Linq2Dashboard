using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// The bars shared by <see cref="RangeFacet{T}"/> and <see cref="DateFacet{T}"/>: one clickable bar per bucket, as a histogram or a list, with the total faintly behind the filtered count and both scaled to the largest total so the shape stays put (design §9).
/// A rendering detail of those two components, not part of the supported API: it is public only because Razor
/// components cannot be internal, is hidden from IntelliSense, and may change without notice.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class BucketBars
{
    /// <summary>The buckets to draw, in order.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<BucketBar> Bars { get; set; } = [];

    /// <summary>Formats the counts.</summary>
    [Parameter, EditorRequired]
    public IDashboardFormatter Formatter { get; set; } = default!;

    /// <summary>Vertical bars with labels beneath, or rows with an inline bar.</summary>
    [Parameter]
    public BucketLayout Layout { get; set; } = BucketLayout.Histogram;

    /// <summary>Show the count on each bar. Default true.</summary>
    [Parameter]
    public bool ShowCounts { get; set; } = true;

    /// <summary>Show the total in parentheses after the filtered count, "filtered (total)". Off by default: the two numbers are equal until another facet narrows the set, and the tooltip always carries both (design §9).</summary>
    [Parameter]
    public bool ShowTotals { get; set; }

    private static string Share(int count, int scale) =>
        ((double)count / scale).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
