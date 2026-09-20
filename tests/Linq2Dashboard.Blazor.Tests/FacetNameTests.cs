using System.Globalization;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>The core's name is a default display name; each facet component may override it (concept §7).</summary>
public class FacetNameTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country).Name("Country");
            b.RangeFacet(x => x.Amount).Name("Amount").Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).Name("Ordered").TimeZone(TestData.Stockholm).Presets(DatePreset.ThisYear);
            b.TextFacet("search", (order, text) => order.Status.Contains(text, StringComparison.OrdinalIgnoreCase)).Name("Find");
        });

    private IRenderedComponent<DashboardView<Order>> RenderWith<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>> configure)
        where TComponent : IComponent =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.AddContent<TComponent>(configure);
        });

    [Fact]
    public void Without_an_override_the_header_shows_the_builder_name()
    {
        var cut = RenderWith<ValueFacet<Order>>(f => f.Add(x => x.Key, "Country"));

        Assert.Equal("Country", cut.Find(".l2d-facet-title").TextContent);
    }

    [Fact]
    public void A_value_facet_name_can_be_overridden()
    {
        var cut = RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.Name, "Land");
        });

        Assert.Equal("Land", cut.Find(".l2d-facet-title").TextContent);
        Assert.Equal(4, cut.FindAll("li.l2d-facet-value").Count);
    }

    [Fact]
    public void A_range_facet_name_can_be_overridden()
    {
        var cut = RenderWith<RangeFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Amount");
            f.Add(x => x.Name, "Belopp");
        });

        Assert.Equal("Belopp", cut.Find(".l2d-facet-title").TextContent);
    }

    [Fact]
    public void A_date_facet_name_can_be_overridden()
    {
        var cut = RenderWith<DateFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "OrderDate");
            f.Add(x => x.Name, "Beställd");
        });

        Assert.Equal("Beställd", cut.Find(".l2d-facet-title").TextContent);
    }

    [Fact]
    public void A_text_facet_name_can_be_overridden_and_labels_the_input()
    {
        var cut = RenderWith<TextFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "search");
            f.Add(x => x.Name, "Sök");
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
            parameters.AddContent(builder =>
            {
                builder.OpenComponent<ValueFacet<Order>>(0);
                builder.AddComponentParameter(1, "Key", "Country");
                builder.AddComponentParameter(2, "Name", "Land");
                builder.CloseComponent();
                builder.OpenComponent<ActiveSelections<Order>>(3);
                builder.CloseComponent();
            });
        });

        Assert.Equal("Land", cut.Find(".l2d-facet-title").TextContent);
        Assert.Equal("Country", cut.Find(".l2d-chip-facet").TextContent);
    }
}
