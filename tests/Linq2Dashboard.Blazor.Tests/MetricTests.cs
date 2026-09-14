using System.Globalization;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class MetricTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Discount);
            b.Count("orders").Title("Orders");
            b.Sum("revenue", x => x.Amount).Title("Revenue");
            b.Average("avgDiscount", x => x.Discount).Title("Average discount");
            b.Distinct("countries", x => x.Country).Title("Countries");
        });

    private IRenderedComponent<DashboardView<Order>> RenderWith<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>> configure, Selections? selections = null)
        where TComponent : IComponent =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            parameters.AddChildContent<TComponent>(configure);
        });

    [Fact]
    public void A_single_metric_renders_its_tile_by_key()
    {
        var cut = RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "revenue"));

        var tile = cut.Find(".l2d-metric");
        Assert.Equal("revenue", tile.GetAttribute("data-key"));
        Assert.Equal("Revenue", tile.QuerySelector(".l2d-metric-title")!.TextContent);
        Assert.Equal("5,424.50", tile.QuerySelector(".l2d-metric-value")!.TextContent);
        Assert.Single(cut.FindAll(".l2d-metric"));
    }

    [Fact]
    public void The_title_can_be_overridden_and_the_value_follows_selections()
    {
        var cut = RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "orders");
            m.Add(x => x.Title, "Order count");
        }, Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal("Order count", cut.Find(".l2d-metric-title").TextContent);
        Assert.Equal("3", cut.Find(".l2d-metric-value").TextContent);
    }

    [Fact]
    public void The_share_of_the_total_is_shown_only_when_asked_for()
    {
        var selections = Selections.Empty.With("Country", ValueSelection.Of("SE"));

        var plain = RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "revenue"), selections);
        Assert.Empty(plain.FindAll(".l2d-metric-share"));

        var withShare = RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "revenue");
            m.Add(x => x.ShowShare, true);
        }, selections);
        Assert.Equal("3,599.50", withShare.Find(".l2d-metric-value").TextContent);
        Assert.Equal("66.4 %", withShare.Find(".l2d-metric-share").TextContent); // 3 599.5 of 5 424.5
    }

    [Fact]
    public void An_average_has_no_share_even_when_asked_for()
    {
        var cut = RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "avgDiscount");
            m.Add(x => x.ShowShare, true);
        });

        Assert.Empty(cut.FindAll(".l2d-metric-share"));
    }

    [Fact]
    public void A_distinct_count_renders_as_a_whole_number_with_its_share()
    {
        var cut = RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "countries");
            m.Add(x => x.ShowShare, true);
        }, Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")));

        Assert.Equal("Countries", cut.Find(".l2d-metric-title").TextContent);
        Assert.Equal("2", cut.Find(".l2d-metric-value").TextContent); // of SE, NO, DK
        Assert.Equal("66.7 %", cut.Find(".l2d-metric-share").TextContent);
    }

    [Fact]
    public void Matching_count_can_show_its_share_of_all_rows()
    {
        var cut = RenderWith<MatchingCount<Order>>(m => m.Add(x => x.ShowShare, true), Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal("3", cut.Find(".l2d-metric-value").TextContent);
        Assert.Equal("37.5 %", cut.Find(".l2d-metric-share").TextContent);
    }

    [Fact]
    public void A_metric_without_a_value_shows_a_dash_and_is_marked()
    {
        var cut = RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "avgDiscount"), Selections.Empty.With("Discount", RangeSelection.OnlyNull));

        Assert.Equal("–", cut.Find(".l2d-metric-value").TextContent);
        Assert.Contains("l2d-metric-empty", cut.Find(".l2d-metric").ClassName);
    }

    [Fact]
    public void A_template_replaces_the_tile_content()
    {
        var cut = RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "revenue");
            m.Add(x => x.MetricTemplate, metric => $"<b class='custom'>{metric.Key}={metric.Value?.ToString(CultureInfo.InvariantCulture)}</b>");
        });

        Assert.Equal("revenue=5424.5", cut.Find(".l2d-metric .custom").TextContent);
        Assert.Empty(cut.FindAll(".l2d-metric-title"));
    }

    [Fact]
    public void Matching_count_is_its_own_tile()
    {
        var cut = RenderWith<MatchingCount<Order>>(m => m.Add(x => x.Title, "Rows"), Selections.Empty.With("Country", ValueSelection.Of("NO")));

        var tile = cut.Find(".l2d-metric");
        Assert.Contains("l2d-metric-matching", tile.ClassName);
        Assert.Equal("Rows", tile.QuerySelector(".l2d-metric-title")!.TextContent);
        Assert.Equal("2", tile.QuerySelector(".l2d-metric-value")!.TextContent);
    }

    [Fact]
    public void Matching_count_defaults_its_title_and_updates_on_click()
    {
        var cut = Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.AddChildContent(builder =>
            {
                builder.OpenComponent<MatchingCount<Order>>(0);
                builder.CloseComponent();
                builder.OpenComponent<ValueFacet<Order>>(1);
                builder.AddComponentParameter(2, nameof(ValueFacet<Order>.Key), "Country");
                builder.CloseComponent();
            });
        });

        Assert.Equal("Matching", cut.Find(".l2d-metric-title").TextContent);
        Assert.Equal("8", cut.Find(".l2d-metric-value").TextContent);

        cut.FindAll("li.l2d-facet-value button").First(b => b.TextContent.Contains("SE")).Click();

        Assert.Equal("3", cut.Find(".l2d-metric-value").TextContent);
    }

    [Fact]
    public void An_unknown_key_fails_clearly()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "nope")));

        Assert.Contains("nope", error.Message);
    }
}
