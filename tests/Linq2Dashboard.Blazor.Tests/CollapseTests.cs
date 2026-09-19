using System.Globalization;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class CollapseTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm).Presets(DatePreset.ThisYear);
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

            parameters.AddContent<TComponent>(configure);
        });

    [Fact]
    public void Facets_are_collapsible_and_expanded_by_default()
    {
        var cut = RenderWith<ValueFacet<Order>>(f => f.Add(x => x.Key, "Country"));

        Assert.Equal("true", cut.Find(".l2d-facet-toggle").GetAttribute("aria-expanded"));
        Assert.Equal(4, cut.FindAll("li.l2d-facet-value").Count);
        Assert.DoesNotContain("l2d-collapsed", cut.Find(".l2d-facet").ClassName);
    }

    [Fact]
    public void Collapsible_false_gives_a_fixed_header()
    {
        var cut = RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.Collapsible, false);
        });

        Assert.Empty(cut.FindAll(".l2d-facet-toggle"));
        Assert.Single(cut.FindAll(".l2d-facet-title"));
        Assert.Equal(4, cut.FindAll("li.l2d-facet-value").Count);
    }

    [Fact]
    public void Collapsible_value_facet_toggles_from_the_header_and_reports_the_change()
    {
        bool? reported = null;
        var cut = RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.Collapsible, true);
            f.Add(x => x.CollapsedChanged, c => reported = c);
        });

        var toggle = cut.Find(".l2d-facet-toggle");
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Country", toggle.QuerySelector(".l2d-facet-title")!.TextContent);

        toggle.Click();

        Assert.True(reported);
        Assert.Empty(cut.FindAll("li.l2d-facet-value"));
        Assert.Empty(cut.FindAll(".l2d-facet-search"));
        Assert.Contains("l2d-collapsed", cut.Find(".l2d-facet").ClassName);
        Assert.Equal("false", cut.Find(".l2d-facet-toggle").GetAttribute("aria-expanded"));

        cut.Find(".l2d-facet-toggle").Click();

        Assert.False(reported);
        Assert.Equal(4, cut.FindAll("li.l2d-facet-value").Count);
    }

    [Fact]
    public void Collapsed_can_be_set_and_changed_by_the_host_without_a_toggle()
    {
        var cut = RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.Collapsible, false);
            f.Add(x => x.Collapsed, true);
        });

        Assert.Empty(cut.FindAll(".l2d-facet-toggle"));
        Assert.Empty(cut.FindAll("li.l2d-facet-value"));
        Assert.Single(cut.FindAll(".l2d-facet-title"));

        cut.Render(parameters => parameters.AddContent<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.Collapsible, false);
            f.Add(x => x.Collapsed, false);
        }));

        Assert.Equal(4, cut.FindAll("li.l2d-facet-value").Count);
    }

    [Fact]
    public void A_collapsed_facet_still_shows_its_clear_link()
    {
        var cut = RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.Collapsed, true);
        }, Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Single(cut.FindAll(".l2d-facet-clear"));
        Assert.Empty(cut.FindAll("li.l2d-facet-value"));
    }

    [Fact]
    public void Range_facet_collapses_bars_and_slider()
    {
        var cut = RenderWith<RangeFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Amount");
            f.Add(x => x.Collapsible, true);
            f.Add(x => x.ShowSlider, true);
        });

        Assert.Equal(4, cut.FindAll("li.l2d-bucket").Count);
        Assert.Single(cut.FindAll(".l2d-slider"));
        Assert.Single(cut.FindAll(".l2d-facet-bounds"));

        cut.Find(".l2d-facet-toggle").Click();

        Assert.Empty(cut.FindAll("li.l2d-bucket"));
        Assert.Empty(cut.FindAll(".l2d-slider"));
        Assert.Single(cut.FindAll(".l2d-facet-bounds")); // header content stays
    }

    [Fact]
    public void Date_facet_collapses_presets_and_periods()
    {
        var cut = RenderWith<DateFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "OrderDate");
            f.Add(x => x.Collapsible, true);
        });

        Assert.Single(cut.FindAll("li.l2d-preset"));
        Assert.Equal(4, cut.FindAll("li.l2d-bucket").Count);

        cut.Find(".l2d-facet-toggle").Click();

        Assert.Empty(cut.FindAll("li.l2d-preset"));
        Assert.Empty(cut.FindAll("li.l2d-bucket"));
        Assert.Contains("l2d-collapsed", cut.Find(".l2d-facet").ClassName);
    }

    [Fact]
    public void The_users_toggle_survives_a_state_change()
    {
        var cut = RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.Collapsible, true);
        });

        cut.Find(".l2d-facet-toggle").Click();
        cut.Render(parameters => parameters.Add(p => p.Selections, Selections.Empty.With("Country", ValueSelection.Of("SE"))));

        Assert.Empty(cut.FindAll("li.l2d-facet-value"));
        Assert.Contains("l2d-collapsed", cut.Find(".l2d-facet").ClassName);
    }
}
