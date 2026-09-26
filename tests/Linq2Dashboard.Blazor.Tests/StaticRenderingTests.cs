using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>
/// Clicks render as links when the view is not interactive and the URL is the state (design §9): static server-side
/// rendering, or the prerender of an interactive page. Each link is the URL of the selections the click would make.
/// </summary>
public class StaticRenderingTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Discount).Buckets(10, 100);
            b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm).Presets(DatePreset.ThisMonth, DatePreset.Last7Days);
        });

    private BunitNavigationManager Navigation => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<DashboardView<Order>> RenderView(
        Action<ComponentParameterCollectionBuilder<DashboardView<Order>>>? content = null,
        bool interactive = false,
        bool syncUrl = true,
        string? key = null,
        string? resetOnChange = null,
        Dashboard<Order>? dashboard = null)
    {
        SetRendererInfo(new RendererInfo(interactive ? "Server" : "Static", interactive));
        return Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, dashboard ?? BuildDashboard());
            parameters.Add(p => p.SyncUrl, syncUrl);
            parameters.Add(p => p.Key, key);
            parameters.Add(p => p.ResetOnChange, resetOnChange);
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            content?.Invoke(parameters);
        });
    }

    private static void ValueFacet(ComponentParameterCollectionBuilder<DashboardView<Order>> parameters, string facetKey = "Country") =>
        parameters.AddContent<ValueFacet<Order>>(facet => facet.Add(f => f.Key, facetKey));

    private static IElement ValueLink(IRenderedComponent<DashboardView<Order>> cut, string label) =>
        cut.FindAll("a.l2d-facet-value-button").Single(a => a.QuerySelector(".l2d-facet-value-label")!.TextContent == label);

    /// <summary>The selections a link leads to, read back through the serializer, so the assertion does not depend on the query's spelling.</summary>
    private static Selections Target(IRenderedComponent<DashboardView<Order>> cut, IElement link, string prefix = "") =>
        cut.Instance.Context.Dashboard.Serializer.FromQueryString(link.GetAttribute("href")!, prefix);

    [Fact]
    public void Values_are_links_to_the_toggled_selections_when_the_view_is_not_interactive()
    {
        Navigation.NavigateTo("page?rows=5");

        var cut = RenderView(p => ValueFacet(p));

        Assert.Empty(cut.FindAll("button:not(.l2d-facet-toggle)"));
        IElement link = ValueLink(cut, "SE");
        Assert.Equal("/page?rows=5&Country=SE", link.GetAttribute("href"));
        Assert.Null(link.GetAttribute("aria-pressed"));
        Assert.True(cut.Instance.Context.Links);
    }

    [Fact]
    public void A_selected_value_links_to_its_removal_and_the_clear_button_is_a_link()
    {
        Navigation.NavigateTo("page?Country=SE,NO");

        var cut = RenderView(p => ValueFacet(p));

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("NO")), Target(cut, ValueLink(cut, "SE")));
        Assert.Equal("/page", cut.Find("a.l2d-facet-clear").GetAttribute("href"));
        Assert.Contains("l2d-selected", ValueLink(cut, "SE").ParentElement!.ClassName);
    }

    [Fact]
    public void The_view_key_prefixes_the_links_parameters()
    {
        Navigation.NavigateTo("page");

        var cut = RenderView(p => ValueFacet(p), key: "o");

        Assert.Equal("/page?o.Country=DK", ValueLink(cut, "DK").GetAttribute("href"));
    }

    [Fact]
    public void Bars_the_null_bar_and_presets_are_links()
    {
        Navigation.NavigateTo("page");
        Dashboard<Order> dashboard = BuildDashboard();
        var discount = (RangeFacetState)dashboard.Calculate().Facet("Discount");
        var dates = (DateFacetState)dashboard.Calculate().Facet("OrderDate");

        var cut = RenderView(p =>
        {
            p.AddContent<RangeFacet<Order>>(facet =>
            {
                facet.Add(f => f.Key, "Discount");
                facet.Add(f => f.Layout, BucketLayout.List);
            });
            p.AddContent<DateFacet<Order>>(facet => facet.Add(f => f.Key, "OrderDate"));
        }, dashboard: dashboard);

        Assert.Empty(cut.FindAll("button:not(.l2d-facet-toggle)"));
        IReadOnlyList<IElement> bars = cut.FindAll("section[data-key='Discount'] a.l2d-bucket-button");
        Assert.Equal(discount.Buckets.Count + 1, bars.Count);
        Assert.Equal(Selections.Empty.ToggleInterval("Discount", discount.Buckets[0].ToInterval()), Target(cut, bars[0]));
        Assert.Equal(Selections.Empty.With("Discount", RangeSelection.Empty.ToggleNull()), Target(cut, bars[^1]));

        IReadOnlyList<IElement> presets = cut.FindAll("a.l2d-preset-button");
        Assert.Equal(2, presets.Count);
        Assert.Equal(Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.Last7Days)), Target(cut, presets[1]));
        Assert.Equal(Selections.Empty.ToggleInterval("OrderDate", dates.Buckets[0].ToInterval()), Target(cut, cut.Find("section[data-key='OrderDate'] a.l2d-bucket-button")));
    }

    [Fact]
    public void Chips_and_clear_all_are_links()
    {
        Navigation.NavigateTo("page?Country=SE,NO&Discount=[10..100)");

        var cut = RenderView(p => p.AddContent<ActiveSelections<Order>>(_ => { }));
        Selections discountOnly = cut.Instance.Context.Dashboard.Serializer.FromQueryString("Discount=[10..100)");

        Assert.Empty(cut.FindAll("button:not(.l2d-facet-toggle)"));
        IReadOnlyList<IElement> removes = cut.FindAll("li[data-key='Country'] a.l2d-chip-value-remove");
        Assert.Equal(2, removes.Count);
        Assert.Equal(discountOnly.With("Country", ValueSelection.Of("NO")), Target(cut, removes[0]));
        Assert.Equal(discountOnly, Target(cut, cut.Find("li[data-key='Country'] a.l2d-chip-remove")));
        Assert.Equal("/page", cut.Find("a.l2d-active-clear-all").GetAttribute("href"));
    }

    [Fact]
    public void The_default_summary_renders_links_too()
    {
        Navigation.NavigateTo("page?Country=SE");

        var cut = RenderView();

        Assert.Empty(cut.FindAll("button:not(.l2d-facet-toggle)"));
        Assert.NotEmpty(cut.FindAll("a.l2d-value"));
        Assert.Equal("/page", cut.Find("a.l2d-clear-all").GetAttribute("href"));
        Assert.Equal("/page", cut.Find("a.l2d-clear").GetAttribute("href"));
    }

    [Fact]
    public void Reset_on_change_drops_the_named_parameters_and_keeps_the_rest()
    {
        Navigation.NavigateTo("page?rows=5&page=3&sort=Amount&direction=desc");

        var cut = RenderView(p => ValueFacet(p), resetOnChange: "page, direction");

        Assert.Equal("/page?rows=5&sort=Amount&Country=SE", ValueLink(cut, "SE").GetAttribute("href"));
    }

    [Fact]
    public void Reset_on_change_also_applies_when_an_interactive_view_writes_the_url()
    {
        Navigation.NavigateTo("page?page=3&rows=5");

        var cut = RenderView(p => ValueFacet(p), interactive: true, resetOnChange: "page");
        cut.FindAll("button.l2d-facet-value-button").Single(b => b.TextContent.Contains("SE")).Click();

        Assert.Equal("http://localhost/page?rows=5&Country=SE", Navigation.Uri);
    }

    [Fact]
    public void An_interactive_view_renders_buttons()
    {
        Navigation.NavigateTo("page");

        var cut = RenderView(p => ValueFacet(p), interactive: true);

        Assert.Empty(cut.FindAll("a"));
        Assert.Equal("false", cut.Find("button.l2d-facet-value-button").GetAttribute("aria-pressed"));
        Assert.False(cut.Instance.Context.Links);
    }

    [Fact]
    public void Without_the_url_as_the_state_a_static_view_still_renders_buttons()
    {
        Navigation.NavigateTo("page");

        var cut = RenderView(p => ValueFacet(p), syncUrl: false);

        Assert.Empty(cut.FindAll("a"));
        Assert.NotEmpty(cut.FindAll("button.l2d-facet-value-button"));
        Assert.False(cut.Instance.Context.Links);
        Assert.Throws<InvalidOperationException>(() => cut.Instance.Context.Href(Selections.Empty));
    }

    [Fact]
    public void The_href_builder_serves_a_host_in_an_interactive_view_as_well()
    {
        Navigation.NavigateTo("page?rows=5#top");

        var cut = RenderView(p => ValueFacet(p), interactive: true);

        Assert.False(cut.Instance.Context.Links);
        Assert.Equal("/page?rows=5&Country=DK#top", cut.Instance.Context.Href(Selections.Empty.With("Country", ValueSelection.Of("DK"))));
    }
}
