using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class DashboardViewTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm).Presets(DatePreset.ThisYear);
            b.CountMetric("orders").Name("Orders");
            b.SumMetric("revenue", x => x.Amount).Name("Revenue");
            b.UseTimeProvider(new FixedTimeProvider(TestData.Instant("2026-03-15T10:00:00Z")));
        });

    private IRenderedComponent<DashboardView<Order>> RenderView(
        Dashboard<Order> dashboard,
        Selections? selections = null,
        Action<Selections>? onChanged = null,
        IDashboardFormatter? formatter = null,
        Action<DashboardState<Order>>? onStateChanged = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, dashboard);
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }

            if (onStateChanged is not null)
            {
                parameters.Add(p => p.StateChanged, onStateChanged);
            }

            // Invariant culture unless a test supplies its own, so expectations do not depend on the machine.
            parameters.Add(p => p.Formatter, formatter ?? new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
        });

    private static IElement ValueButton(IRenderedComponent<DashboardView<Order>> cut, string facetKey, string label) =>
        cut.FindAll($"section[data-key='{facetKey}'] button.l2d-value").Single(b => b.TextContent.Trim() == label);

    [Fact]
    public void Renders_counts_metrics_and_facet_values_from_the_state()
    {
        var cut = RenderView(BuildDashboard());

        Assert.Equal("8", cut.Find(".l2d-matching").TextContent);
        Assert.Equal("8", cut.Find(".l2d-total").TextContent);
        Assert.Equal("8", cut.Find(".l2d-metric[data-key='orders']").TextContent);
        Assert.Equal("5,424.50", cut.Find(".l2d-metric[data-key='revenue']").TextContent);

        var countries = cut.FindAll("section[data-key='Country'] li");
        Assert.Equal(["SE", "NO", "(none)", "DK"], countries.Select(li => li.QuerySelector("button")!.TextContent.Trim()));
        Assert.Equal("3 / 3", countries[0].QuerySelector(".l2d-count")!.TextContent.Trim());
        Assert.Empty(cut.FindAll(".l2d-clear-all"));
    }

    [Fact]
    public void Clicking_a_value_toggles_it_recalculates_and_raises_SelectionsChanged()
    {
        Selections? raised = null;
        var cut = RenderView(BuildDashboard(), onChanged: s => raised = s);

        ValueButton(cut, "Country", "SE").Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), raised);
        Assert.Equal("3", cut.Find(".l2d-matching").TextContent);
        Assert.Contains("l2d-selected", cut.FindAll("section[data-key='Country'] li")[0].ClassName);
        // Status counts follow the Country selection; Country's own counts do not (concept §4.2).
        Assert.Equal("2 / 4", cut.FindAll("section[data-key='Status'] li")[0].QuerySelector(".l2d-count")!.TextContent.Trim());
        Assert.Equal("3 / 3", cut.FindAll("section[data-key='Country'] li")[0].QuerySelector(".l2d-count")!.TextContent.Trim());
        Assert.Single(cut.FindAll(".l2d-clear-all"));

        ValueButton(cut, "Country", "SE").Click();

        Assert.Equal(Selections.Empty, raised);
        Assert.Equal("8", cut.Find(".l2d-matching").TextContent);
    }

    [Fact]
    public void The_null_value_is_clickable_and_labelled_by_the_formatter()
    {
        Selections? raised = null;
        var cut = RenderView(BuildDashboard(), onChanged: s => raised = s);

        ValueButton(cut, "Country", "(none)").Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of([null])), raised);
        Assert.Equal("2", cut.Find(".l2d-matching").TextContent);
    }

    [Fact]
    public void Bucket_and_preset_clicks_use_the_selection_the_state_provides()
    {
        Selections? raised = null;
        var cut = RenderView(BuildDashboard(), onChanged: s => raised = s);

        ValueButton(cut, "Amount", "100 – 500").Click();
        Assert.Equal(Selections.Empty.With("Amount", new RangeSelection(100, 500, toInclusive: false)), raised);
        Assert.Equal("2", cut.Find(".l2d-matching").TextContent);

        ValueButton(cut, "OrderDate", "This year").Click();
        Assert.Equal(DateSelection.Relative(DatePreset.ThisYear), raised!["OrderDate"]);
        Assert.Contains("l2d-selected", cut.Find("section[data-key='OrderDate'] li.l2d-preset").ClassName);
    }

    [Fact]
    public void Clear_buttons_remove_one_facet_or_everything()
    {
        Selections? raised = null;
        var initial = Selections.Empty.With("Country", ValueSelection.Of("SE")).With("Status", ValueSelection.Of("Open"));
        var cut = RenderView(BuildDashboard(), initial, s => raised = s);

        Assert.Equal("2", cut.Find(".l2d-matching").TextContent);

        cut.Find("section[data-key='Country'] .l2d-clear").Click();
        Assert.Equal(Selections.Empty.With("Status", ValueSelection.Of("Open")), raised);

        cut.Find(".l2d-clear-all").Click();
        Assert.Equal(Selections.Empty, raised);
        Assert.Equal("8", cut.Find(".l2d-matching").TextContent);
    }

    [Fact]
    public void Selections_set_by_the_host_are_applied_without_being_echoed_back()
    {
        int raisedCount = 0;
        var cut = RenderView(BuildDashboard(), onChanged: _ => raisedCount++);

        cut.Render(parameters => parameters.Add(p => p.Selections, Selections.Empty.With("Country", ValueSelection.Of("NO"))));

        Assert.Equal("2", cut.Find(".l2d-matching").TextContent);
        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public void StateChanged_delivers_the_new_state_after_the_initial_calculation_and_after_every_click()
    {
        var order = new List<string>();
        var states = new List<DashboardState<Order>>();
        var cut = RenderView(
            BuildDashboard(),
            onChanged: _ => order.Add("selections"),
            onStateChanged: s =>
            {
                order.Add("state");
                states.Add(s);
            });

        Assert.Equal([8], states.Select(s => s.MatchingCount));

        ValueButton(cut, "Country", "SE").Click();

        Assert.Equal([8, 3], states.Select(s => s.MatchingCount));
        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), states[1].Selections);
        Assert.Same(cut.Instance.Context.State, states[1]);
        // Selections first (the cause), then the state (the result).
        Assert.Equal(["state", "selections", "state"], order);
    }

    [Fact]
    public void StateChanged_is_also_raised_for_selections_set_by_the_host()
    {
        var states = new List<DashboardState<Order>>();
        var cut = RenderView(BuildDashboard(), onStateChanged: states.Add);
        var selections = Selections.Empty.With("Country", ValueSelection.Of("NO"));

        cut.Render(parameters => parameters.Add(p => p.Selections, selections));
        Assert.Equal([8, 2], states.Select(s => s.MatchingCount));

        // The same selections again are not a change.
        cut.Render(parameters => parameters.Add(p => p.Selections, selections));
        Assert.Equal(2, states.Count);
    }

    [Fact]
    public void A_custom_formatter_is_used_everywhere()
    {
        var cut = RenderView(BuildDashboard(), formatter: new ShoutingFormatter());

        Assert.Equal("NOTHING", ValueButton(cut, "Country", "NOTHING").TextContent.Trim());
        Assert.Equal("#8", cut.Find(".l2d-matching").TextContent);
        Assert.Equal("~5424.5", cut.Find(".l2d-metric[data-key='revenue']").TextContent);
    }

    [Fact]
    public void Giving_the_view_another_dashboard_recalculates_every_component()
    {
        // The sample's scope switch (concept §4.10): the same view, the same components, a scoped dashboard. Metrics,
        // counts and facets must all follow, although none of them re-renders for a parameter of its own.
        var dashboard = BuildDashboard();
        var scoped = dashboard.ScopeTo(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        DashboardState<Order>? raised = null;
        var cut = RenderView(dashboard, onStateChanged: s => raised = s);
        DashboardContext<Order> context = cut.Instance.Context;

        cut.Render(parameters => parameters.Add(p => p.Dashboard, scoped));

        Assert.Equal("3", cut.Find(".l2d-matching").TextContent);
        Assert.Equal("3", cut.Find(".l2d-total").TextContent);
        Assert.Equal("3", cut.Find(".l2d-metric[data-key='orders']").TextContent);
        Assert.Equal("3,599.50", cut.Find(".l2d-metric[data-key='revenue']").TextContent);
        Assert.Equal(["SE"], cut.FindAll("section[data-key='Country'] li").Select(li => li.QuerySelector("button")!.TextContent.Trim()));
        Assert.Equal(3, raised!.TotalCount);
        Assert.Same(context, cut.Instance.Context);
        Assert.Same(scoped, context.Dashboard);

        // Clicks after the switch go to the new dashboard.
        ValueButton(cut, "Status", "Open").Click();
        Assert.Equal("2", cut.Find(".l2d-matching").TextContent);
        Assert.Equal("3", cut.Find(".l2d-total").TextContent);
    }

    [Fact]
    public void Giving_the_view_another_formatter_reformats_every_component()
    {
        var cut = RenderView(BuildDashboard());
        Assert.Equal("8", cut.Find(".l2d-matching").TextContent);

        cut.Render(parameters => parameters.Add(p => p.Formatter, new ShoutingFormatter()));

        Assert.Equal("#8", cut.Find(".l2d-matching").TextContent);
        Assert.Equal("~5424.5", cut.Find(".l2d-metric[data-key='revenue']").TextContent);
    }

    [Fact]
    public void Custom_child_content_receives_the_context()
    {
        var dashboard = BuildDashboard();
        var cut = Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Dashboard, dashboard)
            .AddContent<Probe>());

        Assert.Equal("8 matching", cut.Find(".probe").TextContent);

        cut.Render(parameters => parameters.Add(p => p.Selections, Selections.Empty.With("Country", ValueSelection.Of("DK"))));

        Assert.Equal("1 matching", cut.Find(".probe").TextContent);
    }

    /// <summary>The child content is a template over the context, so a page hands its own grid the rows without a @ref (design §9).</summary>
    [Fact]
    public void Child_content_is_a_template_over_the_context_and_follows_every_click()
    {
        var cut = Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Dashboard, BuildDashboard())
            .Add(p => p.ChildContent, (RenderFragment<DashboardContext<Order>>)(dash => builder =>
            {
                builder.OpenComponent<ValueFacet<Order>>(0);
                builder.AddComponentParameter(1, nameof(ValueFacet<Order>.Key), "Country");
                builder.CloseComponent();
                builder.OpenElement(2, "span");
                builder.AddAttribute(3, "class", "rows");
                builder.AddContent(4, $"{dash.Items.Count()} rows");
                builder.CloseElement();
            })));

        Assert.Equal("8 rows", cut.Find(".rows").TextContent);

        cut.FindAll("section[data-key='Country'] button.l2d-facet-value-button").Single(b => b.TextContent.Contains("SE")).Click();

        Assert.Equal("3 rows", cut.Find(".rows").TextContent);
    }

    /// <summary>A grid re-queries when its source reference changes, so the context hands out one queryable per state and the same one until then (design §9).</summary>
    [Fact]
    public void The_context_hands_a_grid_one_queryable_per_state()
    {
        var cut = RenderView(BuildDashboard());
        DashboardContext<Order> context = cut.Instance.Context;
        IQueryable<Order> before = context.Items;

        Assert.Equal(8, before.Count());
        Assert.Same(before, context.Items);
        cut.Render();
        Assert.Same(before, context.Items);

        ValueButton(cut, "Country", "SE").Click();

        IQueryable<Order> after = context.Items;
        Assert.NotSame(before, after);
        Assert.Same(after, context.Items);
        Assert.Equal(3, after.Count());
        Assert.Equal(context.State.Items, after);
        Assert.Equal([1, 4, 6], after.OrderBy(o => o.Id).Select(o => o.Id));
    }

    [Fact]
    public void The_context_records_how_long_the_last_calculation_took()
    {
        var cut = RenderView(BuildDashboard());
        DashboardContext<Order> context = cut.Instance.Context;

        Assert.True(context.LastCalculation >= TimeSpan.Zero);
        Assert.True(context.LastCalculation < TimeSpan.FromSeconds(10));

        ValueButton(cut, "Country", "SE").Click();

        Assert.True(context.LastCalculation >= TimeSpan.Zero);
        Assert.True(context.LastCalculation < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_dashboard_component_outside_a_view_fails_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<Probe>());

        Assert.Contains("DashboardView", error.Message);
    }

    private sealed class ShoutingFormatter : DefaultDashboardFormatter
    {
        public override string NullLabel => "NOTHING";

        public override string FormatCount(int count) => $"#{count}";

        public override string FormatMetric(MetricState metric) => $"~{metric.Value?.ToString(CultureInfo.InvariantCulture)}";
    }

    private sealed class Probe : DashboardComponentBase<Order>
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "probe");
            builder.AddContent(2, $"{State.MatchingCount} matching");
            builder.CloseElement();
        }
    }
}
