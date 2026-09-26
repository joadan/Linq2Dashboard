using System.Globalization;
using Bunit;
using Linq2Dashboard.Blazor.Tests.Cascading;
using Linq2Dashboard.Tests;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>
/// Design §9: the view cascades its type parameter, so the components inside it in the same markup need no <c>T</c>,
/// and the view infers its own from <c>Dashboard</c> or <c>Items</c>. The pages under test are Razor files, because
/// the inference is the Razor compiler's; that they compile is half of each test.
/// </summary>
public class CascadingTypeParameterTests : BunitContext
{
    private static readonly IDashboardFormatter Invariant = new DefaultDashboardFormatter(CultureInfo.InvariantCulture);

    [Fact]
    public void Components_inside_a_view_over_a_built_dashboard_take_its_type()
    {
        var dashboard = Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.TextFacet("search", (order, text) => order.Status.Contains(text, StringComparison.OrdinalIgnoreCase));
            b.CountMetric("orders");
        });

        var cut = Render<BuiltPage>(p => p
            .Add(x => x.Dashboard, dashboard)
            .Add(x => x.Formatter, Invariant));

        Assert.IsType<DashboardView<Order>>(cut.FindComponent<DashboardView<Order>>().Instance);
        Assert.Equal("Country", cut.FindComponent<ValueFacet<Order>>().Find(".l2d-facet").GetAttribute("data-key"));
        Assert.Equal("Amount", cut.FindComponent<RangeFacet<Order>>().Find(".l2d-facet").GetAttribute("data-key"));
        Assert.Single(cut.FindComponents<TextFacet<Order>>());
        Assert.Equal("8", cut.Find(".l2d-metric[data-key='orders'] .l2d-metric-value").TextContent);
        Assert.Single(cut.FindComponents<ActiveSelections<Order>>());
        Assert.Single(cut.FindComponents<StateSummary<Order>>());
        Assert.Equal("8", cut.Find(".count").TextContent);
    }

    [Fact]
    public void Definitions_inside_a_view_with_items_take_its_type()
    {
        var cut = Render<ItemsPage>(p => p
            .Add(x => x.Items, TestData.Orders())
            .Add(x => x.Formatter, Invariant));

        var dashboard = cut.FindComponent<DashboardView<Order>>().Instance.Context.Dashboard;
        Assert.Equal(["Status", "search", "country"], dashboard.Facets.Select(f => f.Key));
        Assert.Equal(["orders", "revenue"], dashboard.Metrics.Select(m => m.Key));
        Assert.Equal("8", cut.Find(".l2d-metric[data-key='orders'] .l2d-metric-value").TextContent);
    }

    [Fact]
    public void Another_components_T_is_still_inferred_from_its_own_parameters()
    {
        var cut = Render<ClashPage>(p => p
            .Add(x => x.Items, TestData.Orders())
            .Add(x => x.Formatter, Invariant));

        Assert.Equal(["String", "Int32"], cut.FindAll(".echo").Select(e => e.GetAttribute("data-type")));
        Assert.Equal("text", cut.FindAll(".echo")[0].TextContent);
    }

    [Fact]
    public void Another_component_whose_T_nothing_infers_takes_the_views_type()
    {
        var cut = Render<ClashPage>(p => p
            .Add(x => x.Items, TestData.Orders())
            .Add(x => x.Formatter, Invariant));

        Assert.Equal("Order", cut.Find(".unbound").GetAttribute("data-type"));
    }
}
