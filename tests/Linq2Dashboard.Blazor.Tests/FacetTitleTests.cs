using System.Globalization;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>The core's title is a default display name; each facet component may override it (concept §7).</summary>
public class FacetTitleTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country).Title("Country");
            b.RangeFacet(x => x.Amount).Title("Amount").Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).Title("Ordered").TimeZone(TestData.Stockholm).Presets(DatePreset.ThisYear);
            b.TextFacet("search", (order, text) => order.Status.Contains(text, StringComparison.OrdinalIgnoreCase)).Title("Find");
        });

    private IRenderedComponent<DashboardView<Order>> RenderWith<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>> configure)
        where TComponent : IComponent =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.AddChildContent<TComponent>(configure);
        });

    [Fact]
    public void Without_an_override_the_header_shows_the_builder_title()
    {
        var cut = RenderWith<ValueFacet<Order>>(f => f.Add(x => x.Key, "Country"));

        Assert.Equal("Country", cut.Find(".l2d-facet-title").TextContent);
    }

    [Fact]
    public void A_value_facet_title_can_be_overridden()
    {
        var cut = RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.Title, "Land");
        });

        Assert.Equal("Land", cut.Find(".l2d-facet-title").TextContent);
        Assert.Equal(4, cut.FindAll("li.l2d-facet-value").Count);
    }

    [Fact]
    public void A_range_facet_title_can_be_overridden()
    {
        var cut = RenderWith<RangeFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Amount");
            f.Add(x => x.Title, "Belopp");
        });

        Assert.Equal("Belopp", cut.Find(".l2d-facet-title").TextContent);
    }

    [Fact]
    public void A_date_facet_title_can_be_overridden()
    {
        var cut = RenderWith<DateFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "OrderDate");
            f.Add(x => x.Title, "Beställd");
        });

        Assert.Equal("Beställd", cut.Find(".l2d-facet-title").TextContent);
    }

    [Fact]
    public void A_text_facet_title_can_be_overridden_and_labels_the_input()
    {
        var cut = RenderWith<TextFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "search");
            f.Add(x => x.Title, "Sök");
        });

        Assert.Equal("Sök", cut.Find(".l2d-facet-title").TextContent);
        Assert.Equal("Sök", cut.Find(".l2d-text-input").GetAttribute("aria-label"));
    }

    [Fact]
    public void The_override_does_not_reach_the_state_or_the_chips()
    {
        var cut = Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.Add(p => p.Selections, Selections.Empty.With("Country", ValueSelection.Of("SE")));
            parameters.AddChildContent(builder =>
            {
                builder.OpenComponent<ValueFacet<Order>>(0);
                builder.AddComponentParameter(1, "Key", "Country");
                builder.AddComponentParameter(2, "Title", "Land");
                builder.CloseComponent();
                builder.OpenComponent<ActiveSelections<Order>>(3);
                builder.CloseComponent();
            });
        });

        Assert.Equal("Land", cut.Find(".l2d-facet-title").TextContent);
        Assert.Equal("Country", cut.Find(".l2d-chip-facet").TextContent);
    }
}
