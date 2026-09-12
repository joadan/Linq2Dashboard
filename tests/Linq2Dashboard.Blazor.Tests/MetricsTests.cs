using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class MetricsTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Discount);
            b.Count("orders").Title("Orders");
            b.Sum("revenue", x => x.Amount).Title("Revenue");
            b.Average("avgDiscount", x => x.Discount).Title("Average discount");
        });

    private IRenderedComponent<DashboardView<Order>> RenderMetrics(
        Selections? selections = null, Action<ComponentParameterCollectionBuilder<Metrics<Order>>>? configure = null, IDashboardFormatter? formatter = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, formatter ?? new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            parameters.AddChildContent<Metrics<Order>>(metrics => configure?.Invoke(metrics));
        });

    private static IReadOnlyList<IElement> Tiles(IRenderedComponent<DashboardView<Order>> cut) => cut.FindAll(".l2d-metric");

    private static string Title(IElement tile) => tile.QuerySelector(".l2d-metric-title")!.TextContent.Trim();

    private static string Value(IElement tile) => tile.QuerySelector(".l2d-metric-value")!.TextContent.Trim();

    [Fact]
    public void Renders_every_metric_in_definition_order()
    {
        var cut = RenderMetrics();

        var tiles = Tiles(cut);
        Assert.Equal(["Orders", "Revenue", "Average discount"], tiles.Select(Title));
        Assert.Equal(["8", "5,424.50", "63"], tiles.Select(Value));
        Assert.Equal(["orders", "revenue", "avgDiscount"], tiles.Select(t => t.GetAttribute("data-key")));
    }

    [Fact]
    public void Values_follow_the_selections()
    {
        var cut = RenderMetrics(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(["3", "3,599.50", "86.67"], Tiles(cut).Select(Value));
    }

    [Fact]
    public void A_metric_without_a_value_shows_a_dash_and_is_marked()
    {
        var cut = RenderMetrics(Selections.Empty.With("Discount", RangeSelection.OnlyNull));

        var average = Tiles(cut).Single(t => t.GetAttribute("data-key") == "avgDiscount");
        Assert.Equal("–", Value(average));
        Assert.Contains("l2d-metric-empty", average.ClassName);
        Assert.Equal("3", Value(Tiles(cut)[0])); // count is unaffected by null (concept §4.4)
    }

    [Fact]
    public void Keys_select_and_order_the_tiles()
    {
        var cut = RenderMetrics(configure: m => m.Add(x => x.Keys, new[] { "revenue", "orders" }));

        Assert.Equal(["Revenue", "Orders"], Tiles(cut).Select(Title));
    }

    [Fact]
    public void Matching_count_tile_can_be_included()
    {
        var cut = RenderMetrics(Selections.Empty.With("Country", ValueSelection.Of("NO")), configure: m =>
        {
            m.Add(x => x.IncludeMatchingCount, true);
            m.Add(x => x.MatchingCountTitle, "Rows");
        });

        var first = Tiles(cut)[0];
        Assert.Contains("l2d-metric-matching", first.ClassName);
        Assert.Equal("Rows", Title(first));
        Assert.Equal("2", Value(first));
        Assert.Equal(4, Tiles(cut).Count);
    }

    [Fact]
    public void The_formatter_decides_the_text()
    {
        var cut = RenderMetrics(formatter: new EuroFormatter());

        Assert.Equal("€ 5424.50", Value(Tiles(cut)[1]));
    }

    [Fact]
    public void An_unknown_key_fails_clearly()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderMetrics(configure: m => m.Add(x => x.Keys, new[] { "nope" })));

        Assert.Contains("nope", error.Message);
    }

    private sealed class EuroFormatter : DefaultDashboardFormatter
    {
        public EuroFormatter() : base(CultureInfo.InvariantCulture)
        {
        }

        public override string FormatMetric(MetricState metric) =>
            metric.Aggregation == Aggregation.Sum && metric.Value is double v
                ? $"€ {v.ToString("F2", CultureInfo.InvariantCulture)}"
                : base.FormatMetric(metric);
    }
}
