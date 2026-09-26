using System.Globalization;
using System.Linq.Expressions;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>Metrics defined in markup under a view with <c>Items</c> (concept §7, design §9).</summary>
public class MarkupMetricTests : BunitContext
{
    private int builds;

    private IRenderedComponent<DashboardView<Order>> RenderItems(RenderFragment content, Action<DashboardBuilder<Order>>? build = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Items, TestData.Orders());
            parameters.Add(p => p.Build, b =>
            {
                builds++;
                build?.Invoke(b);
            });
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.AddContent<Order>(content);
        });

    private static RenderFragment Metric(string key, params (string Name, object? Value)[] parameters) => b =>
    {
        b.OpenComponent<Metric<Order>>(0);
        b.AddAttribute(1, "Key", key);
        int sequence = 2;
        foreach ((string name, object? value) in parameters)
        {
            b.AddAttribute(sequence++, name, value);
        }

        b.CloseComponent();
    };

    private static RenderFragment All(params RenderFragment[] parts) => b =>
    {
        for (int i = 0; i < parts.Length; i++)
        {
            b.OpenRegion(i);
            parts[i](b);
            b.CloseRegion();
        }
    };

    private static Expression<Func<Order, object?>> Amount => x => x.Amount;

    private static double? Value(IRenderedComponent<DashboardView<Order>> cut, string key) => cut.Instance.Context.State.Metric(key).Value;

    [Fact]
    public void Each_aggregation_parameter_defines_its_metric()
    {
        var cut = RenderItems(All(
            Metric("orders", ("Count", true), ("Name", "Orders")),
            Metric("revenue", ("Sum", Amount)),
            Metric("average", ("Average", Amount)),
            Metric("smallest", ("Min", Amount)),
            Metric("largest", ("Max", Amount)),
            Metric("countries", ("Distinct", (Expression<Func<Order, object?>>)(x => x.Country)))));

        Assert.Equal(8, Value(cut, "orders"));
        Assert.Equal(5424.5, Value(cut, "revenue"));
        Assert.Equal(5424.5 / 8, Value(cut, "average"));
        Assert.Equal(0, Value(cut, "smallest"));
        Assert.Equal(2500, Value(cut, "largest"));
        Assert.Equal(3, Value(cut, "countries"));
        Assert.Equal("Orders", cut.Instance.Context.Dashboard.Metrics.Single(m => m.Key == "orders").Name);
        Assert.Equal("8", cut.Find(".l2d-metric[data-key='orders'] .l2d-metric-value").TextContent);
        Assert.Equal(2, builds);
    }

    [Fact]
    public void A_formula_reads_plain_metrics_declared_after_it()
    {
        Func<MetricValues, double?> perOrder = m => m["revenue"] / m["orders"];

        var cut = RenderItems(All(
            Metric("perOrder", ("Formula", perOrder)),
            Metric("revenue", ("Sum", Amount)),
            Metric("orders", ("Count", true))));

        Assert.Equal(5424.5 / 8, Value(cut, "perOrder"));
        Assert.Equal(["revenue", "orders", "perOrder"], cut.Instance.Context.Dashboard.Metrics.Select(m => m.Key));
    }

    [Fact]
    public void A_formula_reading_a_metric_defined_nowhere_fails_with_a_hint()
    {
        Func<MetricValues, double?> perOrder = m => m["revenue"] / m["orders"];

        var error = Assert.Throws<InvalidOperationException>(() => RenderItems(All(Metric("perOrder", ("Formula", perOrder)), Metric("orders", ("Count", true)))));

        Assert.Contains("'revenue'", error.Message);
        Assert.Contains("lazy tab", error.Message);
    }

    [Fact]
    public void A_formula_can_read_a_metric_from_Build()
    {
        Func<MetricValues, double?> half = m => m["revenue"] / 2;

        var cut = RenderItems(Metric("half", ("Formula", half)), build: b => b.SumMetric("revenue", x => x.Amount));

        Assert.Equal(5424.5 / 2, Value(cut, "half"));
    }

    [Fact]
    public void Two_aggregations_on_one_metric_fail_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => RenderItems(Metric("x", ("Count", true), ("Sum", Amount))));

        Assert.Contains("Count and Sum", error.Message);
    }

    [Fact]
    public void Define_without_an_aggregation_fails_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RenderItems(Metric("x", ("Define", (Action<MetricBuilder<Order>>)(m => m.Name("X"))))));

        Assert.Contains("no aggregation", error.Message);
    }

    [Fact]
    public void A_metric_Build_defines_is_displayed_and_one_defined_twice_fails()
    {
        var shown = RenderItems(Metric("orders"), build: b => b.CountMetric("orders"));
        Assert.Equal(8, Value(shown, "orders"));

        var error = Assert.Throws<InvalidOperationException>(() => RenderItems(Metric("orders", ("Count", true)), build: b => b.CountMetric("orders")));
        Assert.Contains("Build already defines", error.Message);
    }

    [Fact]
    public void A_metric_Key_before_the_component_that_defines_it_waits()
    {
        var cut = RenderItems(All(Metric("orders"), Metric("orders", ("Count", true))));

        Assert.Equal(2, cut.FindAll(".l2d-metric[data-key='orders']").Count);
    }

    [Fact]
    public void A_facet_and_a_metric_may_share_a_key()
    {
        var cut = RenderItems(All(
            Metric("Country", ("Distinct", (Expression<Func<Order, object?>>)(x => x.Country))),
            b =>
            {
                b.OpenComponent<ValueFacet<Order>>(0);
                b.AddAttribute(1, "For", (Expression<Func<Order, object?>>)(x => x.Country));
                b.CloseComponent();
            }));

        Assert.Equal(3, Value(cut, "Country"));
        Assert.Single(cut.Instance.Context.Dashboard.Facets);
    }

    [Fact]
    public void Switching_the_aggregation_rebuilds_and_a_new_selector_alone_does_not()
    {
        bool average = false;
        Expression<Func<Order, object?>> selector = x => x.Amount;
        var cut = RenderItems(b => Metric("amount", ("Sum", average ? null : selector), ("Average", average ? selector : null))(b));
        Assert.Equal(2, builds);

        selector = x => x.Amount;
        cut.Render();
        Assert.Equal(2, builds);

        average = true;
        cut.Render();
        Assert.Equal(3, builds);
        Assert.Equal(5424.5 / 8, Value(cut, "amount"));
    }
}
