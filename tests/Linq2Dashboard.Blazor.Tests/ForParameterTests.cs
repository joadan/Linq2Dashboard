using System.Globalization;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>
/// Design §9: a facet declared from a member can be named by the same selector, <c>For</c>, which derives the key
/// the builder did (concept §7); <c>Key</c> stays for explicit keys. Errors name the key, the kind and the component.
/// </summary>
public class ForParameterTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm);
            b.TextFacet("search", (order, text) => order.Status.Contains(text, StringComparison.OrdinalIgnoreCase));
        });

    private IRenderedComponent<DashboardView<Order>> RenderWith<TComponent>(
        Action<ComponentParameterCollectionBuilder<TComponent>> configure, Action<Selections>? onChanged = null)
        where TComponent : IComponent =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }

            parameters.AddContent<TComponent>(configure);
        });

    [Fact]
    public void A_value_facet_can_be_named_by_its_selector()
    {
        var cut = RenderWith<ValueFacet<Order>>(f => f.Add(x => x.For, o => o.Country));

        Assert.Equal("Country", cut.Find(".l2d-facet").GetAttribute("data-key"));
        Assert.Equal(4, cut.FindAll("li.l2d-facet-value").Count);
    }

    [Fact]
    public void A_value_type_member_is_looked_through_its_boxing()
    {
        var range = RenderWith<RangeFacet<Order>>(f => f.Add(x => x.For, o => o.Amount));
        var date = RenderWith<DateFacet<Order>>(f => f.Add(x => x.For, o => o.OrderDate));

        Assert.Equal("Amount", range.Find(".l2d-facet").GetAttribute("data-key"));
        Assert.Equal("OrderDate", date.Find(".l2d-facet").GetAttribute("data-key"));
    }

    [Fact]
    public void A_click_on_a_facet_named_by_selector_selects_under_the_derived_key()
    {
        Selections? changed = null;
        var cut = RenderWith<ValueFacet<Order>>(f => f.Add(x => x.For, o => o.Country), s => changed = s);

        cut.FindAll("li.l2d-facet-value button")[0].Click();

        Assert.NotNull(changed);
        Assert.True(changed.Contains("Country"));
    }

    [Fact]
    public void Key_and_For_together_is_an_error()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderWith<ValueFacet<Order>>(f =>
        {
            f.Add(x => x.Key, "Country");
            f.Add(x => x.For, o => o.Country);
        }));

        Assert.Contains("ValueFacet takes either Key or For, not both", error.Message);
    }

    [Fact]
    public void Neither_Key_nor_For_is_an_error()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderWith<RangeFacet<Order>>(_ => { }));

        Assert.Contains("RangeFacet needs a Key, or a For selector", error.Message);
    }

    [Fact]
    public void A_key_of_another_kind_names_the_kind_and_the_component_for_it()
    {
        var range = Assert.ThrowsAny<Exception>(() => RenderWith<RangeFacet<Order>>(f => f.Add(x => x.Key, "Country")));
        var value = Assert.ThrowsAny<Exception>(() => RenderWith<ValueFacet<Order>>(f => f.Add(x => x.For, o => o.Amount)));
        var text = Assert.ThrowsAny<Exception>(() => RenderWith<TextFacet<Order>>(f => f.Add(x => x.Key, "OrderDate")));
        var date = Assert.ThrowsAny<Exception>(() => RenderWith<DateFacet<Order>>(f => f.Add(x => x.Key, "search")));

        Assert.Contains("Facet 'Country' is a Value facet, which RangeFacet does not render; use ValueFacet.", range.Message);
        Assert.Contains("Facet 'Amount' is a Range facet, which ValueFacet does not render; use RangeFacet.", value.Message);
        Assert.Contains("Facet 'OrderDate' is a Date facet, which TextFacet does not render; use DateFacet.", text.Message);
        Assert.Contains("Facet 'search' is a Text facet, which DateFacet does not render; use TextFacet.", date.Message);
    }

    [Fact]
    public void An_unknown_key_suggests_the_case_only_match()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderWith<ValueFacet<Order>>(f => f.Add(x => x.Key, "country")));

        Assert.Contains("Did you mean 'Country'?", error.Message);
    }
}
