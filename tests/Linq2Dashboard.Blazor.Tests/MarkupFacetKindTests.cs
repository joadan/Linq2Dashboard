using System.Globalization;
using System.Linq.Expressions;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>Range, date and text facets defined in markup under a view with <c>Items</c> (concept §7, design §9).</summary>
public class MarkupFacetKindTests : BunitContext
{
    private int builds;

    private IRenderedComponent<DashboardView<Order>> RenderItems(RenderFragment content, Selections? selections = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Items, TestData.Orders());
            parameters.Add(p => p.Build, _ => builds++);
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            parameters.AddContent<Order>(content);
        });

    private static RenderFragment Component<TComponent>(Expression<Func<Order, object?>>? @for, params (string Name, object? Value)[] parameters)
        where TComponent : IComponent => b =>
    {
        b.OpenComponent<TComponent>(0);
        if (@for is not null)
        {
            b.AddAttribute(1, "For", @for);
        }

        int sequence = 2;
        foreach ((string name, object? value) in parameters)
        {
            b.AddAttribute(sequence++, name, value);
        }

        b.CloseComponent();
    };

    private static FacetInfo Facet(IRenderedComponent<DashboardView<Order>> cut, string key) =>
        cut.Instance.Context.Dashboard.Facets.Single(f => f.Key == key);

    [Fact]
    public void A_RangeFacet_with_For_defines_a_range_facet_with_its_cut_points()
    {
        var cut = RenderItems(Component<RangeFacet<Order>>(x => x.Amount, ("Buckets", new double[] { 100, 500, 1000 })));

        Assert.Equal(FacetKind.Range, Facet(cut, "Amount").Kind);
        Assert.Equal(4, ((RangeFacetState)cut.Instance.Context.State.Facet("Amount")).Buckets.Count);
        Assert.NotEmpty(cut.FindAll("section[data-key='Amount'] .l2d-bucket"));
    }

    [Fact]
    public void Buckets_and_AutoBuckets_together_fail_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RenderItems(Component<RangeFacet<Order>>(x => x.Amount, ("Buckets", new double[] { 100 }), ("AutoBuckets", 5))));

        Assert.Contains("not both", error.Message);
    }

    [Fact]
    public void A_range_over_a_non_numeric_member_fails_with_the_builder_message()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderItems(Component<RangeFacet<Order>>(x => x.Status)));

        Assert.Contains("not a numeric type", error.Message);
    }

    [Fact]
    public void Buckets_are_watched_by_content()
    {
        double[] cuts = [100, 500, 1000];
        var cut = RenderItems(b => Component<RangeFacet<Order>>(x => x.Amount, ("Buckets", cuts.ToArray()))(b));
        Assert.Equal(2, builds);

        cut.Render();
        Assert.Equal(2, builds);

        cuts = [100, 1000];
        cut.Render();
        Assert.Equal(3, builds);
        Assert.Equal(3, ((RangeFacetState)cut.Instance.Context.State.Facet("Amount")).Buckets.Count);
    }

    [Fact]
    public void A_DateFacet_with_For_defines_a_date_facet_with_its_options()
    {
        var cut = RenderItems(Component<DateFacet<Order>>(x => x.OrderDate,
            ("Granularity", DateGranularity.Month),
            ("TimeZone", TestData.Stockholm),
            ("Presets", new[] { DatePreset.ThisYear })));

        var state = (DateFacetState)cut.Instance.Context.State.Facet("OrderDate");
        Assert.Equal(FacetKind.Date, Facet(cut, "OrderDate").Kind);
        Assert.Equal(DateGranularity.Month, state.Granularity);
        Assert.Single(state.Presets);
    }

    [Fact]
    public void Granularity_and_AutoGranularity_together_fail_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RenderItems(Component<DateFacet<Order>>(x => x.OrderDate, ("Granularity", DateGranularity.Month), ("AutoGranularity", 12))));

        Assert.Contains("not both", error.Message);
    }

    [Fact]
    public void A_TextFacet_with_Match_defines_a_text_facet_and_applies_a_held_selection()
    {
        Func<Order, string, bool> match = (o, text) => o.Status.Contains(text, StringComparison.OrdinalIgnoreCase);

        var cut = RenderItems(
            Component<TextFacet<Order>>(null, ("Key", "search"), ("Match", match), ("Name", "Search")),
            Selections.Empty.With("search", new TextSelection("open")));

        Assert.Equal(FacetKind.Text, Facet(cut, "search").Kind);
        Assert.Equal("Search", Facet(cut, "search").Name);
        Assert.Equal(4, cut.Instance.Context.State.MatchingCount);
        Assert.Equal("open", cut.Find("section[data-key='search'] input").GetAttribute("value"));
    }

    [Fact]
    public void A_TextFacet_with_Define_but_no_Match_fails_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RenderItems(Component<TextFacet<Order>>(null, ("Key", "search"), ("Define", (Action<TextFacetBuilder<Order>>)(_ => { })))));

        Assert.Contains("no Match", error.Message);
    }

    [Fact]
    public void Match_under_a_prebuilt_Dashboard_fails_clearly()
    {
        Func<Order, string, bool> match = (_, _) => true;

        var error = Assert.Throws<InvalidOperationException>(() => Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Dashboard, Dashboard.Create(TestData.Orders(), b => b.TextFacet("search", match)))
            .AddContent<Order>(Component<TextFacet<Order>>(null, ("Key", "search"), ("Match", match)))));

        Assert.Contains("need a DashboardView with Items", error.Message);
    }
}
