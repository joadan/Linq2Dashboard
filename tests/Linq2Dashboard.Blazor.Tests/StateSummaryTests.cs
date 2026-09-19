using System.Globalization;
using Bunit;
using Linq2Dashboard.Tests;

namespace Linq2Dashboard.Blazor.Tests;

public class StateSummaryTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country).Top(2);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.CountMetric("orders");
        });

    private IRenderedComponent<DashboardView<Order>> RenderSummary(Selections? selections = null, Action<ComponentParameterCollectionBuilder<StateSummary<Order>>>? configure = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            parameters.AddContent<StateSummary<Order>>(summary => configure?.Invoke(summary));
        });

    [Fact]
    public void Renders_counts_metrics_and_every_facet_with_english_defaults()
    {
        var cut = RenderSummary(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal("3", cut.Find(".l2d-matching").TextContent.Trim());
        Assert.Equal("8", cut.Find(".l2d-total").TextContent.Trim());
        Assert.Contains(" of", cut.Find(".l2d-summary-counts").TextContent);
        Assert.Equal("Clear all", cut.Find(".l2d-clear-all").TextContent.Trim());
        Assert.Equal("3", cut.Find(".l2d-metric[data-key=orders]").TextContent.Trim());
        Assert.Equal("Clear", cut.Find(".l2d-summary-facet[data-key=Country] .l2d-clear").TextContent.Trim());
        Assert.StartsWith("Other", cut.Find(".l2d-other").TextContent.Trim());
        Assert.Equal(2, cut.FindAll(".l2d-summary-facet").Count);
    }

    [Fact]
    public void Every_text_is_a_parameter()
    {
        var cut = RenderSummary(Selections.Empty.With("Country", ValueSelection.Of("SE")), summary =>
        {
            summary.Add(s => s.OfText, "av");
            summary.Add(s => s.ClearAllText, "Rensa alla");
            summary.Add(s => s.ClearText, "Rensa");
            summary.Add(s => s.OtherText, "Övriga");
        });

        Assert.Contains("av", cut.Find(".l2d-summary-counts").TextContent);
        Assert.Equal("Rensa alla", cut.Find(".l2d-clear-all").TextContent.Trim());
        Assert.Equal("Rensa", cut.Find(".l2d-clear").TextContent.Trim());
        Assert.StartsWith("Övriga", cut.Find(".l2d-other").TextContent.Trim());
    }

    [Fact]
    public void Clicking_a_value_and_clearing_go_through_the_context()
    {
        var cut = RenderSummary();

        cut.Find(".l2d-summary-facet[data-key=Country] .l2d-value").Click();
        Assert.Single(cut.FindAll(".l2d-summary-facet[data-key=Country] li.l2d-selected"));

        cut.Find(".l2d-clear-all").Click();
        Assert.Empty(cut.FindAll("li.l2d-selected"));
        Assert.Empty(cut.FindAll(".l2d-clear-all"));
    }
}
