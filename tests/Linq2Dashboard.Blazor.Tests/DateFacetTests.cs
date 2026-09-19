using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class DateFacetTests : BunitContext
{
    // OrderDate in Stockholm: Jan 15, Feb 3, Feb 20, Mar 1 (23:30), Mar 10, Mar 31 (22:30), Apr 2, Apr 15 → Jan 1, Feb 2, Mar 3, Apr 2.
    // Shipped: three nulls (rows 1, 4, 6).  Now: Sunday 15 March 2026.
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.DateFacet(x => x.OrderDate).Name("Ordered").TimeZone(TestData.Stockholm).Presets(DatePreset.ThisMonth, DatePreset.Last7Days);
            b.DateFacet(x => x.Shipped);
            b.UseTimeProvider(new FixedTimeProvider(TestData.Instant("2026-03-15T10:00:00Z")));
        });

    private IRenderedComponent<DashboardView<Order>> RenderFacet(
        string key, Selections? selections = null, Action<Selections>? onChanged = null, Action<ComponentParameterCollectionBuilder<DateFacet<Order>>>? configure = null) =>
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

            parameters.AddContent<DateFacet<Order>>(facet =>
            {
                facet.Add(f => f.Key, key);
                configure?.Invoke(facet);
            });
        });

    private static IReadOnlyList<IElement> Buckets(IRenderedComponent<DashboardView<Order>> cut) => cut.FindAll("li.l2d-bucket");

    private static IReadOnlyList<IElement> Presets(IRenderedComponent<DashboardView<Order>> cut) => cut.FindAll("li.l2d-preset");

    private static string Label(IElement item) => item.QuerySelector(".l2d-bucket-label, .l2d-preset-label")!.TextContent.Trim();

    private static string Count(IElement item) => item.QuerySelector(".l2d-bucket-count, .l2d-preset-count")!.TextContent.Trim();

    [Fact]
    public void Renders_periods_in_order_with_counts_and_presets_with_counts()
    {
        var cut = RenderFacet("OrderDate");

        Assert.Equal("Ordered", cut.Find(".l2d-facet-title").TextContent);
        Assert.Equal(["Jan 2026", "Feb 2026", "Mar 2026", "Apr 2026"], Buckets(cut).Select(Label));
        Assert.Equal(["1 (1)", "2 (2)", "3 (3)", "2 (2)"], Buckets(cut).Select(Count));
        Assert.Equal(["This month", "Last 7 days"], Presets(cut).Select(Label));
        Assert.Equal(["3 (3)", "1 (1)"], Presets(cut).Select(Count));
        Assert.Equal(["3 (3) 100.0 %", "1 (1) 100.0 %"], Presets(cut).Select(p => p.QuerySelector("button")!.GetAttribute("title")));
        Assert.Empty(cut.FindAll(".l2d-facet-clear"));
    }

    [Fact]
    public void Clicking_a_period_selects_its_interval_and_clicking_again_clears()
    {
        Selections? raised = null;
        var cut = RenderFacet("OrderDate", onChanged: s => raised = s);

        Buckets(cut)[2].QuerySelector("button")!.Click();

        var expected = DateSelection.Between(TestData.Instant("2026-03-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00"));
        Assert.Equal(Selections.Empty.With("OrderDate", expected), raised);
        Assert.Contains("l2d-selected", Buckets(cut)[2].ClassName);
        // "This month" is the same interval, so it lights up too.
        Assert.Contains("l2d-selected", Presets(cut)[0].ClassName);

        Buckets(cut)[2].QuerySelector("button")!.Click();
        Assert.Equal(Selections.Empty, raised);
    }

    [Fact]
    public void Clicking_a_preset_selects_the_relative_preset_and_clicking_again_clears()
    {
        Selections? raised = null;
        var cut = RenderFacet("OrderDate", onChanged: s => raised = s);

        Presets(cut)[1].QuerySelector("button")!.Click();

        Assert.Equal(Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.Last7Days)), raised);
        Assert.Contains("l2d-selected", Presets(cut)[1].ClassName);
        Assert.Equal("true", Presets(cut)[1].QuerySelector("button")!.GetAttribute("aria-pressed"));
        // Last 7 days is 9 to 16 March: inside March but not covering it, so no period is selected.
        Assert.DoesNotContain(Buckets(cut), b => b.ClassList.Contains("l2d-selected"));

        Presets(cut)[1].QuerySelector("button")!.Click();
        Assert.Equal(Selections.Empty, raised);
    }

    [Fact]
    public void A_wider_interval_marks_every_period_it_covers()
    {
        var selection = DateSelection.Between(TestData.Instant("2026-02-01T00:00:00+01:00"), null);
        var cut = RenderFacet("OrderDate", Selections.Empty.With("OrderDate", selection));

        Assert.Equal([false, true, true, true], Buckets(cut).Select(b => b.ClassList.Contains("l2d-selected")));
        // Presets light up only for an exactly equal interval, never by coverage (design §2.4).
        Assert.DoesNotContain("l2d-selected", Presets(cut)[0].ClassName);
    }

    [Fact]
    public void Counts_follow_other_facets_selections()
    {
        var cut = RenderFacet("OrderDate", Selections.Empty.With("Country", ValueSelection.Of("SE")));

        // SE orders: Jan 15, Mar 1, Mar 31. Totals are shown by default.
        Assert.Equal(["1 (1)", "0 (2)", "2 (3)", "0 (2)"], Buckets(cut).Select(Count));
        Assert.Equal(["2 (3)", "0 (1)"], Presets(cut).Select(Count));
        Assert.Contains("l2d-zero", Buckets(cut)[1].ClassName);
    }

    [Fact]
    public void ShowTotals_off_shows_the_filtered_count_alone()
    {
        var cut = RenderFacet("OrderDate", Selections.Empty.With("Country", ValueSelection.Of("SE")), configure: f => f.Add(x => x.ShowTotals, false));

        Assert.Equal(["1", "0", "2", "0"], Buckets(cut).Select(Count));
        Assert.Equal(["2", "0"], Presets(cut).Select(Count));
        Assert.Empty(cut.FindAll(".l2d-bucket-total, .l2d-preset-total"));
    }

    [Fact]
    public void The_null_bucket_appears_only_with_nulls_and_toggles_only_null()
    {
        Assert.DoesNotContain(Buckets(RenderFacet("OrderDate")), b => b.ClassList.Contains("l2d-null"));

        Selections? raised = null;
        var cut = RenderFacet("Shipped", onChanged: s => raised = s);
        var nullBucket = Buckets(cut).Single(b => b.ClassList.Contains("l2d-null"));
        Assert.Equal("(none)", Label(nullBucket));
        Assert.Equal("3 (3)", Count(nullBucket));
        Assert.Empty(Presets(cut));

        nullBucket.QuerySelector("button")!.Click();
        Assert.Equal(Selections.Empty.With("Shipped", DateSelection.OnlyNull), raised);

        Buckets(cut).Single(b => b.ClassList.Contains("l2d-null")).QuerySelector("button")!.Click();
        Assert.Equal(Selections.Empty, raised);
    }

    [Fact]
    public void Clear_removes_the_facets_selection()
    {
        Selections? raised = null;
        var selections = Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.ThisMonth)).With("Country", ValueSelection.Of("SE"));
        var cut = RenderFacet("OrderDate", selections, s => raised = s);

        cut.Find(".l2d-facet-clear").Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), raised);
    }

    [Fact]
    public void Layout_and_options()
    {
        var cut = RenderFacet("OrderDate", configure: f =>
        {
            f.Add(x => x.Layout, BucketLayout.List);
            f.Add(x => x.ShowPresets, false);
            f.Add(x => x.ShowCounts, false);
        });

        Assert.Contains("l2d-date-list", cut.Find(".l2d-date-facet").ClassName);
        Assert.Contains("l2d-buckets-list", cut.Find(".l2d-buckets").ClassName);
        Assert.Empty(Presets(cut));
        Assert.Empty(cut.FindAll(".l2d-bucket-count"));
    }

    [Fact]
    public void Wrong_kind_fails_clearly()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderFacet("Country"));

        Assert.Contains("not a date facet", error.Message);
    }
}
