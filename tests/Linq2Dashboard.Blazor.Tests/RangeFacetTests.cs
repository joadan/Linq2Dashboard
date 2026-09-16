using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class RangeFacetTests : BunitContext
{
    // Amount: 100, 250, 500, 999.5, 1000, 2500, 0, 75  → buckets <100: 2, 100–500: 2, 500–1000: 2, ≥1000: 2, no nulls
    // Discount: 10, null, 50, 0, null, 250, null, 5   → buckets <10: 2, 10–100: 2, ≥100: 1, null: 3
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Name("Order amount").Buckets(100, 500, 1000);
            b.RangeFacet(x => x.Discount).Buckets(10, 100);
        });

    private IRenderedComponent<DashboardView<Order>> RenderFacet(
        string key, Selections? selections = null, Action<Selections>? onChanged = null, Action<ComponentParameterCollectionBuilder<RangeFacet<Order>>>? configure = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }

            parameters.AddChildContent<RangeFacet<Order>>(facet =>
            {
                facet.Add(f => f.Key, key);
                configure?.Invoke(facet);
            });
        });

    private static IReadOnlyList<IElement> Buckets(IRenderedComponent<DashboardView<Order>> cut) => cut.FindAll("li.l2d-bucket");

    private static string Label(IElement bucket) => bucket.QuerySelector(".l2d-bucket-label")!.TextContent.Trim();

    private static string Count(IElement bucket) => bucket.QuerySelector(".l2d-bucket-count")!.TextContent.Trim();

    private static string Style(IElement bucket) => bucket.GetAttribute("style")!.Replace(" ", "");

    [Fact]
    public void Renders_name_bounds_buckets_and_counts()
    {
        var cut = RenderFacet("Amount");

        Assert.Equal("Order amount", cut.Find(".l2d-facet-title").TextContent);
        Assert.Equal("0 – 2,500", cut.Find(".l2d-facet-bounds").TextContent);
        var buckets = Buckets(cut);
        Assert.Equal(["< 100", "100 – 500", "500 – 1,000", "≥ 1,000"], buckets.Select(Label));
        Assert.Equal(["2 (2)", "2 (2)", "2 (2)", "2 (2)"], buckets.Select(Count));
        Assert.Contains("l2d-range-histogram", cut.Find(".l2d-range-facet").ClassName);
        Assert.Empty(cut.FindAll(".l2d-facet-clear"));
    }

    [Fact]
    public void Bars_scale_to_the_largest_total_and_show_filtered_over_total()
    {
        var cut = RenderFacet("Discount", Selections.Empty.With("Country", ValueSelection.Of("SE")));

        // Discount totals 2, 2, 1, null 3 → scale 3. SE rows have discounts 10, 0, 250: filtered 1, 1, 1, null 0.
        var buckets = Buckets(cut);
        Assert.Equal("--l2d-total:0.667;--l2d-filtered:0.333;", Style(buckets[0]));
        Assert.Equal("--l2d-total:0.333;--l2d-filtered:0.333;", Style(buckets[2]));
        Assert.Equal("--l2d-total:1;--l2d-filtered:0;", Style(buckets[3]));
        Assert.Contains("l2d-zero", buckets[3].ClassName);
    }

    [Fact]
    public void Clicking_a_bucket_selects_its_interval_and_clicking_again_clears()
    {
        Selections? raised = null;
        var cut = RenderFacet("Amount", onChanged: s => raised = s);

        Buckets(cut)[1].QuerySelector("button")!.Click();

        Assert.Equal(Selections.Empty.With("Amount", new RangeSelection(100, 500, toInclusive: false)), raised);
        Assert.Contains("l2d-selected", Buckets(cut)[1].ClassName);
        Assert.Equal("true", Buckets(cut)[1].QuerySelector("button")!.GetAttribute("aria-pressed"));
        Assert.Single(cut.FindAll(".l2d-facet-clear"));
        // The facet's own counts do not change on its own selection (concept §4.2).
        Assert.Equal(["2 (2)", "2 (2)", "2 (2)", "2 (2)"], Buckets(cut).Select(Count));

        Buckets(cut)[1].QuerySelector("button")!.Click();

        Assert.Equal(Selections.Empty, raised);
        Assert.Empty(cut.FindAll(".l2d-selected"));
    }

    [Fact]
    public void A_wider_interval_marks_every_bucket_it_covers()
    {
        var cut = RenderFacet("Amount", Selections.Empty.With("Amount", RangeSelection.AtLeast(500)));

        Assert.Equal([false, false, true, true], Buckets(cut).Select(b => b.ClassList.Contains("l2d-selected")));
    }

    [Fact]
    public void The_null_bucket_appears_only_when_there_are_nulls_and_toggles_only_null()
    {
        Assert.DoesNotContain(Buckets(RenderFacet("Amount")), b => b.ClassList.Contains("l2d-null"));

        Selections? raised = null;
        var cut = RenderFacet("Discount", onChanged: s => raised = s);
        var nullBucket = Buckets(cut).Single(b => b.ClassList.Contains("l2d-null"));
        Assert.Equal("(none)", Label(nullBucket));
        Assert.Equal("3 (3)", Count(nullBucket));

        nullBucket.QuerySelector("button")!.Click();
        Assert.Equal(Selections.Empty.With("Discount", RangeSelection.OnlyNull), raised);
        Assert.Contains("l2d-selected", Buckets(cut).Single(b => b.ClassList.Contains("l2d-null")).ClassName);

        Buckets(cut).Single(b => b.ClassList.Contains("l2d-null")).QuerySelector("button")!.Click();
        Assert.Equal(Selections.Empty, raised);
    }

    [Fact]
    public void Counts_follow_other_facets_selections()
    {
        var cut = RenderFacet("Amount", Selections.Empty.With("Country", ValueSelection.Of("SE")));

        // SE amounts: 100, 999.5, 2500. Totals are shown by default.
        Assert.Equal(["0 (2)", "1 (2)", "1 (2)", "1 (2)"], Buckets(cut).Select(Count));
    }

    [Fact]
    public void ShowTotals_off_shows_the_filtered_count_alone()
    {
        var cut = RenderFacet("Amount", Selections.Empty.With("Country", ValueSelection.Of("SE")), configure: f => f.Add(x => x.ShowTotals, false));

        Assert.Equal(["0", "1", "1", "1"], Buckets(cut).Select(Count));
        Assert.Empty(cut.FindAll(".l2d-bucket-total"));
    }

    [Fact]
    public void Bucket_tooltip_shows_label_counts_and_the_filtered_share_of_the_total()
    {
        var cut = RenderFacet("Amount", Selections.Empty.With("Country", ValueSelection.Of("SE")));

        var titles = Buckets(cut).Select(b => b.QuerySelector("button")!.GetAttribute("title")!).ToList();
        Assert.EndsWith(": 0 (2) 0.0 %", titles[0]);
        Assert.EndsWith(": 1 (2) 50.0 %", titles[1]);
    }

    [Fact]
    public void Clear_removes_the_facets_selection()
    {
        Selections? raised = null;
        var cut = RenderFacet("Amount", Selections.Empty.With("Amount", RangeSelection.AtLeast(500)).With("Country", ValueSelection.Of("SE")), s => raised = s);

        cut.Find(".l2d-facet-clear").Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), raised);
    }

    [Fact]
    public void List_layout_and_options()
    {
        var cut = RenderFacet("Amount", configure: f =>
        {
            f.Add(x => x.Layout, BucketLayout.List);
            f.Add(x => x.ShowBounds, false);
            f.Add(x => x.ShowCounts, false);
        });

        Assert.Contains("l2d-range-list", cut.Find(".l2d-range-facet").ClassName);
        Assert.Empty(cut.FindAll(".l2d-facet-bounds"));
        Assert.Empty(cut.FindAll(".l2d-bucket-count"));
        Assert.Equal(4, Buckets(cut).Count);
    }

    [Fact]
    public void Wrong_kind_fails_clearly()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderFacet("Country"));

        Assert.Contains("not a range facet", error.Message);
    }
}
