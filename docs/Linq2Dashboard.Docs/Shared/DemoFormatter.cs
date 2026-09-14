using Linq2Dashboard.Blazor;

namespace Linq2Dashboard.Docs.Shared;

/// <summary>
/// The demo's formatter: metric tiles show whole numbers, so an average or a revenue per customer
/// reads as an amount rather than a calculation. Everything else is the library default.
/// </summary>
public sealed class DemoFormatter : DefaultDashboardFormatter
{
    public static new DemoFormatter Instance { get; } = new();

    public override string FormatMetric(MetricState metric) =>
        metric.Value is double value ? Math.Round(value).ToString("N0", Culture) : "–";
}
