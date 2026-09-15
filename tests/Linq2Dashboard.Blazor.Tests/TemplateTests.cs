using System.Globalization;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class TemplateTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm);
            b.Count("orders").Title("Orders");
            b.Sum("revenue", x => x.Amount).Title("Revenue");
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
    public void Value_facet_header_template_replaces_the_default_header()
    {
        var cut = RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.HeaderTemplate, facet => $"<div class='custom-header'>{facet.Title} ({facet.DistinctCount})</div>");
        });

        Assert.Equal("Country (4)", cut.Find(".custom-header").TextContent);
        Assert.Empty(cut.FindAll(".l2d-facet-header"));
    }

    [Fact]
    public void Value_facet_value_template_replaces_label_and_count_but_keeps_the_click()
    {
        Selections? raised = null;
        var cut = Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.Add(p => p.SelectionsChanged, s => raised = s);
            parameters.AddChildContent<ValueFacet<Order>>(f =>
            {
                f.Add(x => x.Key, "Country");
                f.Add(x => x.ValueTemplate, value => $"<b class='custom-value'>{value.Value ?? "none"}={value.FilteredCount}</b>");
            });
        });

        var custom = cut.FindAll(".custom-value");
        Assert.Equal(["SE=3", "NO=2", "none=2", "DK=1"], custom.Select(c => c.TextContent));
        Assert.Empty(cut.FindAll(".l2d-facet-value-label"));

        cut.FindAll("li.l2d-facet-value button")[0].Click();
        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), raised);
    }

    [Fact]
    public void Range_and_date_facets_accept_a_header_template()
    {
        var range = RenderWith<RangeFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Amount");
            f.Add(x => x.HeaderTemplate, facet => $"<div class='custom-header'>{facet.Min}-{facet.Max}</div>");
        });
        Assert.Equal("0-2500", range.Find(".custom-header").TextContent);
        Assert.Empty(range.FindAll(".l2d-facet-title"));
        Assert.Equal(4, range.FindAll("li.l2d-bucket").Count);

        var date = RenderWith<DateFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "OrderDate");
            f.Add(x => x.HeaderTemplate, facet => $"<div class='custom-header'>{facet.Granularity}</div>");
        });
        Assert.Equal("Month", date.Find(".custom-header").TextContent);
        Assert.Equal(4, date.FindAll("li.l2d-bucket").Count);
    }

    [Fact]
    public void Default_headers_keep_the_shared_structure()
    {
        var cut = RenderWith<RangeFacet<Order>>(f => f.Add(x => x.Key, "Amount"), Selections.Empty.With("Amount", RangeSelection.AtLeast(500)));

        var header = cut.Find(".l2d-facet-header");
        Assert.Equal("Amount", header.QuerySelector(".l2d-facet-title")!.TextContent);
        Assert.Equal("0 – 2,500", header.QuerySelector(".l2d-facet-bounds")!.TextContent);
        var clear = header.QuerySelector(".l2d-facet-clear")!;
        Assert.Equal("×", clear.TextContent);
        Assert.Equal("Clear", clear.GetAttribute("aria-label"));
    }

}
