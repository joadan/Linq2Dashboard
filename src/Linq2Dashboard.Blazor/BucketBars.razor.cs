using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

public partial class BucketBars
{
    [Parameter, EditorRequired]
    public IReadOnlyList<BucketBar> Bars { get; set; } = [];

    [Parameter, EditorRequired]
    public IDashboardFormatter Formatter { get; set; } = default!;

    [Parameter]
    public BucketLayout Layout { get; set; } = BucketLayout.Histogram;

    [Parameter]
    public bool ShowCounts { get; set; } = true;

    [Parameter]
    public bool ShowTotals { get; set; }

    private static string Share(int count, int scale) =>
        ((double)count / scale).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
