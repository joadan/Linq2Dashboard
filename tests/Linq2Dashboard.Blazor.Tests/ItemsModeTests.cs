using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>A view that builds its own dashboard over <c>Items</c> with <c>Build</c> (concept §7, design §9).</summary>
public class ItemsModeTests : BunitContext
{
    private int builds;

    private void CountryAndOrders(DashboardBuilder<Order> b)
    {
        builds++;
        b.ValueFacet(x => x.Country);
        b.ValueFacet(x => x.Status);
        b.CountMetric("orders");
    }

    private IRenderedComponent<DashboardView<Order>> RenderItems(
        IReadOnlyList<Order> items,
        Action<DashboardBuilder<Order>>? build = null,
        object? rebuildKey = null,
        Action<Selections>? onChanged = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Items, items);
            parameters.Add(p => p.Build, build ?? CountryAndOrders);
            if (rebuildKey is not null)
            {
                parameters.Add(p => p.RebuildKey, rebuildKey);
            }

            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }

            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
        });

    private static IElement ValueButton(IRenderedComponent<DashboardView<Order>> cut, string facetKey, string label) =>
        cut.FindAll($"section[data-key='{facetKey}'] button.l2d-value").Single(b => b.TextContent.Trim() == label);

    [Fact]
    public void Items_with_Build_renders_a_dashboard_the_view_built()
    {
        var cut = RenderItems(TestData.Orders());

        Assert.Equal(1, builds);
        Assert.Equal("8", cut.Find(".l2d-matching").TextContent);
        Assert.Equal("8", cut.Find(".l2d-metric[data-key='orders']").TextContent);
        Assert.Equal("3 (3)", cut.FindAll("section[data-key='Country'] li")[0].QuerySelector(".l2d-count")!.TextContent.Trim());
    }

    [Fact]
    public void Items_without_Build_is_a_dashboard_without_facets_or_metrics()
    {
        var cut = Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Items, TestData.Orders())
            .Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture)));

        Assert.Equal("8", cut.Find(".l2d-matching").TextContent);
        Assert.Empty(cut.Instance.Context.Dashboard.Facets);
    }

    [Fact]
    public void A_re_render_with_the_same_list_does_not_build_again()
    {
        Order[] orders = TestData.Orders();
        var cut = RenderItems(orders);
        ValueButton(cut, "Country", "SE").Click();

        cut.Render(parameters => parameters.Add(p => p.Items, orders));
        cut.Render();

        Assert.Equal(1, builds);
        Assert.Equal("3", cut.Find(".l2d-matching").TextContent);
    }

    [Fact]
    public void A_new_list_builds_again_and_keeps_the_selections()
    {
        Selections? raised = null;
        var cut = RenderItems(TestData.Orders(), onChanged: s => raised = s);
        ValueButton(cut, "Country", "SE").Click();
        raised = null;

        Order[] fewer = TestData.Orders().Where(o => o.Id <= 4).ToArray();
        cut.Render(parameters => parameters.Add(p => p.Items, fewer));

        Assert.Equal(2, builds);
        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), cut.Instance.Context.Selections);
        Assert.Equal("2", cut.Find(".l2d-matching").TextContent);
        Assert.Equal("4", cut.Find(".l2d-total").TextContent);
        Assert.Null(raised);
    }

    [Fact]
    public void A_changed_RebuildKey_builds_again_over_the_same_list()
    {
        Order[] orders = TestData.Orders();
        var cut = RenderItems(orders, rebuildKey: "sv");

        cut.Render(parameters => parameters.Add(p => p.RebuildKey, "sv"));
        Assert.Equal(1, builds);

        cut.Render(parameters => parameters.Add(p => p.RebuildKey, "en"));
        Assert.Equal(2, builds);
    }

    [Fact]
    public void A_rebuild_drops_selections_the_new_definition_cannot_read_and_reports_it()
    {
        bool withStatus = true;
        void Build(DashboardBuilder<Order> b)
        {
            b.ValueFacet(x => x.Country);
            if (withStatus)
            {
                b.ValueFacet(x => x.Status);
            }
        }

        Selections? raised = null;
        var cut = RenderItems(TestData.Orders(), Build, rebuildKey: 1, onChanged: s => raised = s);
        ValueButton(cut, "Country", "SE").Click();
        ValueButton(cut, "Status", "Open").Click();

        withStatus = false;
        cut.Render(parameters => parameters.Add(p => p.RebuildKey, 2));

        Selections expected = Selections.Empty.With("Country", ValueSelection.Of("SE"));
        Assert.Equal(expected, cut.Instance.Context.Selections);
        Assert.Equal(expected, raised);
        Assert.Equal("3", cut.Find(".l2d-matching").TextContent);
    }

    [Fact]
    public void Dashboard_and_Items_together_fail_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Dashboard, Dashboard.Create(TestData.Orders(), CountryAndOrders))
            .Add(p => p.Items, TestData.Orders())));

        Assert.Contains("not both", error.Message);
    }

    [Fact]
    public void Neither_Dashboard_nor_Items_fails_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<DashboardView<Order>>());

        Assert.Contains("Dashboard", error.Message);
        Assert.Contains("Items", error.Message);
    }

    [Fact]
    public void Build_or_RebuildKey_with_a_prebuilt_Dashboard_fails_clearly()
    {
        Dashboard<Order> dashboard = Dashboard.Create(TestData.Orders(), CountryAndOrders);

        var withBuild = Assert.Throws<InvalidOperationException>(() => Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Dashboard, dashboard)
            .Add(p => p.Build, CountryAndOrders)));
        var withKey = Assert.Throws<InvalidOperationException>(() => Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Dashboard, dashboard)
            .Add(p => p.RebuildKey, 1)));

        Assert.Contains("apply to Items", withBuild.Message);
        Assert.Contains("apply to Items", withKey.Message);
    }
}
