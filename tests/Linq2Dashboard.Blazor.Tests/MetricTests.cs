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
            b.Count("orders").Name("Orders");
            b.Sum("revenue", x => x.Amount).Name("Revenue");
            b.Average("avgDiscount", x => x.Discount).Name("Average discount");
            b.Distinct("countries", x => x.Country).Name("Countries");
            b.Calculated("aov", m => m["revenue"] / m["orders"]).Name("Average order");
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
    public void The_name_can_be_overridden_and_the_value_follows_selections()
    {
        var cut = RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "orders");
            m.Add(x => x.Name, "Order count");
        }, Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal("Order count", cut.Find(".l2d-metric-title").TextContent);
        Assert.Equal("3", cut.Find(".l2d-metric-value").TextContent);
    }

    [Fact]
    public void The_share_of_the_total_is_shown_under_the_value()
    {
        var cut = RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "revenue"), Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal("3,599.50", cut.Find(".l2d-metric-value").TextContent);
        Assert.Equal("66.4 %", cut.Find(".l2d-metric-share").TextContent); // 3 599.5 of 5 424.5
    }

    [Fact]
    public void An_average_has_no_share()
    {
        var cut = RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "avgDiscount"));

        Assert.Empty(cut.FindAll(".l2d-metric-share"));
    }

    [Fact]
    public void A_distinct_count_renders_as_a_whole_number_with_its_share()
    {
        var cut = RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "countries"), Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")));

        Assert.Equal("Countries", cut.Find(".l2d-metric-title").TextContent);
        Assert.Equal("2", cut.Find(".l2d-metric-value").TextContent); // of SE, NO, DK
        Assert.Equal("66.7 %", cut.Find(".l2d-metric-share").TextContent);
    }

    [Fact]
    public void A_calculated_metric_renders_like_any_other_and_has_no_share()
    {
        var cut = RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "aov"));

        Assert.Equal("Average order", cut.Find(".l2d-metric-title").TextContent);
        Assert.Equal("678.06", cut.Find(".l2d-metric-value").TextContent); // 5 424.5 / 8
        Assert.Empty(cut.FindAll(".l2d-metric-share"));
    }

    [Fact]
    public void A_count_metric_shows_the_matching_rows_and_their_share_of_all_rows()
    {
        // There is no matching-count component: a Count metric is the matching row count (design §9.5).
        var cut = RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "orders"), Selections.Empty.With("Country", ValueSelection.Of("SE")));

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
    public void A_template_replaces_the_whole_tile_and_owns_the_root()
    {
        var cut = RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "revenue");
            m.Add(x => x.MetricTemplate, tile => $"<b class='custom'>{tile.Metric.Key}={tile.Value}</b>");
        });

        var custom = cut.Find(".custom");
        Assert.Equal("revenue=5,424.50", custom.TextContent);
        Assert.Contains("l2d-dashboard", custom.ParentElement!.ClassName);
        Assert.Empty(cut.FindAll(".l2d-metric"));
        Assert.Empty(cut.FindAll(".l2d-metric-title"));
    }

    [Fact]
    public void A_count_metric_updates_on_click()
    {
        var cut = Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.AddChildContent(builder =>
            {
                builder.OpenComponent<Metric<Order>>(0);
                builder.AddComponentParameter(1, nameof(Metric<Order>.Key), "orders");
                builder.CloseComponent();
                builder.OpenComponent<ValueFacet<Order>>(2);
                builder.AddComponentParameter(3, nameof(ValueFacet<Order>.Key), "Country");
                builder.CloseComponent();
            });
        });

        Assert.Equal("Orders", cut.Find(".l2d-metric-title").TextContent);
        Assert.Equal("8", cut.Find(".l2d-metric-value").TextContent);

        cut.FindAll("li.l2d-facet-value button").First(b => b.TextContent.Contains("SE")).Click();

        Assert.Equal("3", cut.Find(".l2d-metric-value").TextContent);
    }

    [Fact]
    public void A_template_receives_the_formatted_pieces_and_the_state()
    {
        MetricTileContent? seen = null;
        RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "revenue");
            m.Add(x => x.MetricTemplate, tile => { seen = tile; return ""; });
        }, Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.NotNull(seen);
        Assert.Equal("Revenue", seen.Name);
        var formatter = new DefaultDashboardFormatter(CultureInfo.InvariantCulture);
        Assert.Equal(formatter.FormatMetric(seen.Metric), seen.Value);
        Assert.Equal(formatter.FormatShare(seen.Metric.Share!.Value), seen.Share);
        Assert.False(seen.IsEmpty);
        Assert.Equal(Aggregation.Sum, seen.Metric.Aggregation);
    }

    [Fact]
    public void A_template_sees_the_empty_value_and_no_share_when_there_is_none()
    {
        MetricTileContent? seen = null;
        RenderWith<Metric<Order>>(m =>
        {
            m.Add(x => x.Key, "avgDiscount");
            m.Add(x => x.Name, "Discount");
            m.Add(x => x.MetricTemplate, tile => { seen = tile; return ""; });
        }, Selections.Empty.With("Discount", RangeSelection.OnlyNull));

        Assert.NotNull(seen);
        Assert.Equal("Discount", seen.Name);
        Assert.Equal("–", seen.Value);
        Assert.Null(seen.Share);
        Assert.True(seen.IsEmpty);
    }

    [Fact]
    public void An_unknown_key_fails_clearly()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderWith<Metric<Order>>(m => m.Add(x => x.Key, "nope")));

        Assert.Contains("nope", error.Message);
    }
}
