namespace Linq2Dashboard.Tests;

public class CalculateTests
{
    // Now is Sunday 15 March 2026, 11:00 in Stockholm.
    private static readonly FixedTimeProvider Clock = new(TestData.Instant("2026-03-15T10:00:00Z"));

    // Row: Country Status  Active Amount Discount OrderDate (Stockholm)
    //  0:  SE      Open    T      100    10       15 Jan
    //  1:  NO      Open    T      250    null      3 Feb
    //  2:  null    Closed  F      500    50       20 Feb
    //  3:  SE      Closed  T      999.5  0         1 Mar 23:30
    //  4:  DK      Pending F      1000   null     10 Mar
    //  5:  se      Open    T      2500   250      31 Mar 22:30
    //  6:  null    Pending F      0      null      2 Apr
    //  7:  NO      Open    T      75     5        15 Apr
    private static Dashboard<Order> Build(Action<DashboardBuilder<Order>>? extra = null) =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.BooleanFacet(x => x.IsActive);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.RangeFacet(x => x.Discount).Buckets(10, 100);
            b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm).Presets(DatePreset.ThisMonth, DatePreset.Last7Days);
            b.Count("orders");
            b.Sum("revenue", x => x.Amount);
            b.Average("avgDiscount", x => x.Discount);
            b.OrderByDescending(x => x.Amount);
            b.UseTimeProvider(Clock);
            extra?.Invoke(b);
        });

    private static readonly Dashboard<Order> Shared = Build();

    private static ValueFacetState Values(DashboardState<Order> state, string key) => (ValueFacetState)state.Facet(key);

    private static (object? Value, int Total, int Filtered, bool Selected)[] Flatten(IEnumerable<FacetValue> values) =>
        values.Select(v => (v.Value, v.TotalCount, v.FilteredCount, v.Selected)).ToArray();

    [Fact]
    public void With_no_selections_everything_matches_and_filtered_equals_total()
    {
        var state = Shared.Calculate();

        Assert.Equal(8, state.TotalCount);
        Assert.Equal(8, state.MatchingCount);
        Assert.True(state.Selections.IsEmpty);

        var country = Values(state, "Country");
        Assert.Equal(8, country.ContextCount);
        Assert.Null(country.Other);
        Assert.Equal(4, country.DistinctCount);
        Assert.Equal(
            [("SE", 3, 3, false), ("NO", 2, 2, false), (null, 2, 2, false), ("DK", 1, 1, false)],
            Flatten(country.Values));

        Assert.Equal(8, state.Metric("orders").Value);
        Assert.Equal(5424.5, state.Metric("revenue").Value);
        Assert.Equal(63.0, state.Metric("avgDiscount").Value); // (10 + 50 + 0 + 250 + 5) / 5
    }

    [Fact]
    public void A_facets_own_selection_is_excluded_from_its_own_counts()
    {
        var state = Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(3, state.MatchingCount);

        var country = Values(state, "Country");
        Assert.Equal(8, country.ContextCount);
        Assert.Equal(
            [("SE", 3, 3, true), ("NO", 2, 2, false), (null, 2, 2, false), ("DK", 1, 1, false)],
            Flatten(country.Values));

        var status = Values(state, "Status");
        Assert.Equal(3, status.ContextCount);
        Assert.Equal(
            [("Open", 4, 2, false), ("Closed", 2, 1, false), ("Pending", 2, 0, false)],
            Flatten(status.Values));

        var active = Values(state, "IsActive");
        Assert.Equal([(true, 5, 3, false), (false, 3, 0, false)], Flatten(active.Values));
    }

    [Fact]
    public void Each_facet_sees_every_other_facets_selection()
    {
        var state = Shared.Calculate(Selections.Empty
            .With("Country", ValueSelection.Of("SE"))
            .With("Status", ValueSelection.Of("Open")));

        Assert.Equal(2, state.MatchingCount);

        // Country counts use Status = Open only (rows 0, 1, 5, 7).
        Assert.Equal(
            [("SE", 3, 2, true), ("NO", 2, 2, false), (null, 2, 0, false), ("DK", 1, 0, false)],
            Flatten(Values(state, "Country").Values));

        // Status counts use Country = SE only (rows 0, 3, 5).
        Assert.Equal(
            [("Open", 4, 2, true), ("Closed", 2, 1, false), ("Pending", 2, 0, false)],
            Flatten(Values(state, "Status").Values));

        // IsActive has no selection and uses both (rows 0, 5).
        var active = Values(state, "IsActive");
        Assert.Equal(2, active.ContextCount);
        Assert.Equal([(true, 5, 2, false), (false, 3, 0, false)], Flatten(active.Values));
    }

    [Fact]
    public void Zero_count_values_stay_in_the_state()
    {
        var state = Shared.Calculate(Selections.Empty.With("Status", ValueSelection.Of("Pending")));

        var country = Values(state, "Country");
        Assert.Contains(country.Values, v => Equals(v.Value, "SE") && v.FilteredCount == 0 && v.TotalCount == 3);
        Assert.Equal(4, country.Values.Count);
    }

    [Fact]
    public void Filtered_counts_always_sum_to_the_context_count()
    {
        Selections[] cases =
        [
            Selections.Empty,
            Selections.Empty.With("Country", ValueSelection.Of("SE", null)),
            Selections.Empty.With("Status", ValueSelection.Of("Open")).With("Amount", RangeSelection.AtLeast(100)),
            Selections.Empty.With("Discount", RangeSelection.OnlyNull).With("OrderDate", DateSelection.Relative(DatePreset.ThisMonth)),
            Selections.Empty.With("IsActive", ValueSelection.Of(false)).With("Country", ValueSelection.Of("FI")),
        ];

        foreach (Selections selections in cases)
        {
            var state = Shared.Calculate(selections);
            foreach (FacetState facet in state.Facets)
            {
                int sum = facet switch
                {
                    ValueFacetState v => v.Values.Sum(x => x.FilteredCount) + (v.Other?.FilteredCount ?? 0),
                    RangeFacetState r => r.Buckets.Sum(x => x.FilteredCount) + r.Null.FilteredCount,
                    DateFacetState d => d.Buckets.Sum(x => x.FilteredCount) + d.Null.FilteredCount,
                    _ => throw new InvalidOperationException(),
                };

                Assert.Equal(facet.ContextCount, sum);
            }
        }
    }

    [Fact]
    public void Top_N_truncates_ranks_and_reports_other_against_the_context()
    {
        var dashboard = Build(b => b.ValueFacet("top", x => x.Country).Top(2));

        var state = dashboard.Calculate();
        var top = Values(state, "top");
        Assert.Equal([("SE", 3, 3, false), ("NO", 2, 2, false)], Flatten(top.Values));
        Assert.Equal(new FacetCount(3, 3), top.Other);
        Assert.Equal(4, top.DistinctCount);

        // A selected value is always presented, and Other is measured against this facet's context.
        var withDk = dashboard.Calculate(Selections.Empty.With("top", ValueSelection.Of("DK")).With("Status", ValueSelection.Of("Open")));
        var topDk = Values(withDk, "top");
        Assert.Equal(4, topDk.ContextCount); // Status = Open rows
        Assert.Equal([("SE", 3, 2, false), ("DK", 1, 0, true)], Flatten(topDk.Values));
        Assert.Equal(new FacetCount(8 - 4, 4 - 2), topDk.Other);
    }

    [Fact]
    public void Rank_mode_changes_which_values_are_presented()
    {
        var dashboard = Build(b =>
        {
            b.ValueFacet("byFiltered", x => x.Country).Top(2);
            b.ValueFacet("byTotal", x => x.Country).Top(2).RankBy(RankMode.TotalCount);
        });

        // Status = Pending is rows 4 (DK) and 6 (null): filtered DK 1, null 1, SE 0, NO 0.
        var state = dashboard.Calculate(Selections.Empty.With("Status", ValueSelection.Of("Pending")));

        Assert.Equal([(null, 2, 1, false), ("DK", 1, 1, false)], Flatten(Values(state, "byFiltered").Values));
        Assert.Equal([("SE", 3, 0, false), (null, 2, 1, false)], Flatten(Values(state, "byTotal").Values));
    }

    [Fact]
    public void Top_N_larger_than_the_value_count_presents_everything_without_other()
    {
        var dashboard = Build(b => b.ValueFacet("top", x => x.Country).Top(10));

        var top = Values(dashboard.Calculate(), "top");

        Assert.Equal(4, top.Values.Count);
        Assert.Null(top.Other);
    }

    [Fact]
    public void Range_facet_state_has_fixed_buckets_and_selected_flags_by_coverage()
    {
        var state = Shared.Calculate();
        var amount = (RangeFacetState)state.Facet("Amount");

        Assert.Equal(0, amount.Min);
        Assert.Equal(2500, amount.Max);
        Assert.Equal(
            [
                new RangeBucket(double.NegativeInfinity, 100, 2, 2, false),
                new RangeBucket(100, 500, 2, 2, false),
                new RangeBucket(500, 1000, 2, 2, false),
                new RangeBucket(1000, double.PositiveInfinity, 2, 2, false),
            ],
            amount.Buckets);
        Assert.Equal(new FacetValue(null, 0, 0, false), amount.Null);

        // Clicking the second bucket selects exactly its rows; the facet's own counts do not change.
        var clicked = Shared.Calculate(Selections.Empty.With("Amount", amount.Buckets[1].ToSelection()));
        var clickedAmount = (RangeFacetState)clicked.Facet("Amount");
        Assert.Equal(2, clicked.MatchingCount);
        Assert.Equal([2, 1], clicked.Items.Select(o => o.Id)); // amount descending: 250, then 100
        Assert.Equal([false, true, false, false], clickedAmount.Buckets.Select(b => b.Selected));
        Assert.Equal([2, 2, 2, 2], clickedAmount.Buckets.Select(b => b.FilteredCount));

        // A wider interval covers several buckets; a partial one covers none.
        var wide = (RangeFacetState)Shared.Calculate(Selections.Empty.With("Amount", RangeSelection.AtLeast(500))).Facet("Amount");
        Assert.Equal([false, false, true, true], wide.Buckets.Select(b => b.Selected));
        var partial = (RangeFacetState)Shared.Calculate(Selections.Empty.With("Amount", RangeSelection.Between(200, 700))).Facet("Amount");
        Assert.All(partial.Buckets, b => Assert.False(b.Selected));
    }

    [Fact]
    public void Range_facet_null_value_is_counted_and_selectable()
    {
        var state = Shared.Calculate(Selections.Empty.With("Discount", RangeSelection.OnlyNull));
        var discount = (RangeFacetState)state.Facet("Discount");

        Assert.Equal(3, state.MatchingCount);
        Assert.Equal(new FacetValue(null, 3, 3, true), discount.Null);
        Assert.All(discount.Buckets, b => Assert.False(b.Selected));

        var other = (RangeFacetState)Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("NO"))).Facet("Discount");
        Assert.Equal(new FacetValue(null, 3, 1, false), other.Null); // row 1 is NO with null discount
    }

    [Fact]
    public void Date_facet_state_has_period_buckets_and_resolved_presets()
    {
        var state = Shared.Calculate();
        var dates = (DateFacetState)state.Facet("OrderDate");

        Assert.Equal(DateGranularity.Month, dates.Granularity);
        Assert.Same(TestData.Stockholm, dates.TimeZone);
        Assert.Equal(
            [new DateTime(2026, 1, 1), new DateTime(2026, 2, 1), new DateTime(2026, 3, 1), new DateTime(2026, 4, 1)],
            dates.Buckets.Select(b => b.PeriodStart));
        Assert.Equal([1, 2, 3, 2], dates.Buckets.Select(b => b.TotalCount));
        Assert.Equal(TestData.Instant("2026-03-01T00:00:00+01:00"), dates.Buckets[2].From);
        Assert.Equal(TestData.Instant("2026-04-01T00:00:00+02:00"), dates.Buckets[2].To);

        Assert.Equal(
            [
                new PresetState(DatePreset.ThisMonth, TestData.Instant("2026-03-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00"), 3, 3, false),
                new PresetState(DatePreset.Last7Days, TestData.Instant("2026-03-09T00:00:00+01:00"), TestData.Instant("2026-03-16T00:00:00+01:00"), 1, 1, false),
            ],
            dates.Presets);
        Assert.Equal(new FacetValue(null, 0, 0, false), dates.Null);
    }

    [Fact]
    public void Date_facet_counts_follow_other_selections_and_flags_follow_its_own()
    {
        var state = Shared.Calculate(Selections.Empty
            .With("Country", ValueSelection.Of("SE"))
            .With("OrderDate", DateSelection.Relative(DatePreset.ThisMonth)));
        var dates = (DateFacetState)state.Facet("OrderDate");

        Assert.Equal(2, state.MatchingCount); // rows 3 and 5
        Assert.Equal(3, dates.ContextCount);
        Assert.Equal([1, 0, 2, 0], dates.Buckets.Select(b => b.FilteredCount));
        Assert.Equal([false, false, true, false], dates.Buckets.Select(b => b.Selected));
        Assert.Equal([(true, 2), (false, 0)], dates.Presets.Select(p => (p.Selected, p.FilteredCount)));

        var absolute = (DateFacetState)Shared.Calculate(Selections.Empty.With("OrderDate",
            DateSelection.Between(TestData.Instant("2026-01-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00")))).Facet("OrderDate");
        Assert.Equal([true, true, true, false], absolute.Buckets.Select(b => b.Selected));
        Assert.All(absolute.Presets, p => Assert.False(p.Selected)); // covers March but is not exactly "This month"

        var marchExactly = (DateFacetState)Shared.Calculate(Selections.Empty.With("OrderDate", dates.Buckets[2].ToSelection())).Facet("OrderDate");
        Assert.True(marchExactly.Presets[0].Selected); // the March bar is exactly "This month" (design §2.4)
        Assert.False(marchExactly.Presets[1].Selected);

        var clicked = Shared.Calculate(Selections.Empty.With("OrderDate", dates.Buckets[1].ToSelection()));
        Assert.Equal([2, 3], clicked.Items.Select(o => o.Id).Order());
    }

    [Fact]
    public void Metrics_follow_all_selections_and_report_null_when_nothing_contributes()
    {
        var se = Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Equal(3, se.Metric("orders").Value);
        Assert.Equal(3599.5, se.Metric("revenue").Value);
        Assert.Equal(260.0 / 3, se.Metric("avgDiscount").Value!.Value, precision: 10);

        var none = Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("FI")));
        Assert.Equal(0, none.MatchingCount);
        Assert.Equal(0, none.Metric("orders").Value);
        Assert.Null(none.Metric("revenue").Value);
        Assert.False(none.Metric("avgDiscount").HasValue);

        var noDiscounts = Shared.Calculate(Selections.Empty.With("Discount", RangeSelection.OnlyNull));
        Assert.Equal(3, noDiscounts.Metric("orders").Value);
        Assert.Null(noDiscounts.Metric("avgDiscount").Value);
        Assert.Equal(new MetricState("revenue", "revenue", Aggregation.Sum, 1250, 1250 / 5424.5), noDiscounts.Metric("revenue"));
    }

    [Fact]
    public void Count_and_sum_metrics_report_their_share_of_the_total()
    {
        var all = Shared.Calculate();
        Assert.Equal(1.0, all.Metric("orders").Share);
        Assert.Equal(1.0, all.Metric("revenue").Share);
        Assert.False(all.Metric("avgDiscount").HasShare); // an average has no total to be a part of

        var se = Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Equal(3 / 8.0, se.Metric("orders").Share);
        Assert.Equal(3599.5 / 5424.5, se.Metric("revenue").Share);
        Assert.Null(se.Metric("avgDiscount").Share);

        var none = Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("FI")));
        Assert.Equal(0.0, none.Metric("orders").Share);
        Assert.Null(none.Metric("revenue").Share); // no value, so no share

        var minMax = Build(b =>
        {
            b.Min("cheapest", x => x.Amount);
            b.Max("dearest", x => x.Amount);
        }).Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Null(minMax.Metric("cheapest").Share);
        Assert.Null(minMax.Metric("dearest").Share);
    }

    [Fact]
    public void A_share_of_a_zero_total_is_undefined()
    {
        var empty = Dashboard.Create(Array.Empty<Order>(), b =>
        {
            b.Count("orders");
            b.Sum("revenue", x => x.Amount);
        }).Calculate();
        Assert.Equal(0, empty.Metric("orders").Value);
        Assert.Null(empty.Metric("orders").Share);
        Assert.Null(empty.Metric("revenue").Share);

        var cancelling = Dashboard.Create([new Signed(1, 10), new Signed(2, -10)], b =>
        {
            b.ValueFacet(x => x.Id);
            b.Sum("net", x => x.Amount);
        }).Calculate(Selections.Empty.With("Id", ValueSelection.Of(1)));
        Assert.Equal(10, cancelling.Metric("net").Value);
        Assert.Null(cancelling.Metric("net").Share);
    }

    private sealed record Signed(int Id, decimal Amount);

    [Fact]
    public void Pages_follow_the_application_order()
    {
        var state = Shared.Calculate();

        Assert.Equal(3, state.PageCount(3));
        Assert.Equal([6, 5, 4], state.GetPage(0, 3).Items.Select(o => o.Id));
        Assert.Equal([3, 2, 1], state.GetPage(1, 3).Items.Select(o => o.Id));
        var last = state.GetPage(2, 3);
        Assert.Equal([8, 7], last.Items.Select(o => o.Id));
        Assert.Equal((2, 3, 8, 3, true, false), (last.PageIndex, last.PageSize, last.MatchingCount, last.PageCount, last.HasPrevious, last.HasNext));
        Assert.Empty(state.GetPage(3, 3).Items);
        Assert.Equal([6, 5, 4, 3, 2, 1, 8, 7], state.Items.Select(o => o.Id));
    }

    [Fact]
    public void Pages_contain_only_matching_rows()
    {
        var state = Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal([6, 4], state.GetPage(0, 2).Items.Select(o => o.Id));
        Assert.Equal([1], state.GetPage(1, 2).Items.Select(o => o.Id));
        Assert.Equal([6, 4, 1], state.Items.Select(o => o.Id));
        Assert.Equal(2, state.PageCount(2));
    }

    [Fact]
    public void Without_a_sort_order_pages_follow_row_order()
    {
        var unsorted = Dashboard.Create(TestData.Orders(), b => b.ValueFacet(x => x.Country));

        var state = unsorted.Calculate(Selections.Empty.With("Country", ValueSelection.Of("NO", null)));

        Assert.Equal([2, 3, 7, 8], state.Items.Select(o => o.Id));
        Assert.Equal([2, 3], state.GetPage(0, 2).Items.Select(o => o.Id));
    }

    [Fact]
    public void GetItems_slices_the_ordered_matching_rows()
    {
        var state = Shared.Calculate();

        Assert.Equal([6, 5, 4], state.GetItems(0, 3).Select(o => o.Id));
        Assert.Equal([2, 1, 8], state.GetItems(4, 3).Select(o => o.Id));
        Assert.Equal([7], state.GetItems(7, 5).Select(o => o.Id));
        Assert.Empty(state.GetItems(8, 5));
        Assert.Empty(state.GetItems(3, 0));
        Assert.Equal(state.GetPage(1, 3).Items, state.GetItems(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetItems(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetItems(0, -1));
    }

    [Fact]
    public void Page_arguments_are_validated()
    {
        var state = Shared.Calculate();

        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetPage(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetPage(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.PageCount(0));
    }

    [Fact]
    public void Search_returns_matching_values_ranked_with_current_counts()
    {
        var dashboard = Build(b => b.ValueFacet("search", x => x.Status).Searchable());
        var state = dashboard.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        var search = Values(state, "search");

        Assert.True(search.IsSearchable);
        Assert.Equal([("Open", 4, 2, false), ("Pending", 2, 0, false)], Flatten(search.Search("EN")));
        Assert.Equal([("Open", 4, 2, false)], Flatten(search.Search("en", max: 1)));
        Assert.Empty(search.Search("zzz"));
        Assert.Empty(Values(state, "Country").Search("null")); // the null value is never returned
        Assert.Throws<ArgumentOutOfRangeException>(() => search.Search("a", 0));
    }

    [Fact]
    public void Unknown_keys_in_selections_are_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => Shared.Calculate(Selections.Empty.With("Nope", ValueSelection.Of(1))));
        Assert.Contains("Nope", error.Message);
        Assert.Throws<ArgumentException>(() => Shared.Calculate().Facet("Nope"));
        Assert.Throws<ArgumentException>(() => Shared.Calculate().Metric("Nope"));
        Assert.Throws<ArgumentNullException>(() => Shared.Calculate(null!));
    }

    [Fact]
    public void Equal_selections_return_the_cached_state()
    {
        var dashboard = Build();
        var a = Selections.Empty.With("Country", ValueSelection.Of("SE", "NO"));
        var b = Selections.Empty.With("Country", ValueSelection.Of("NO", "SE"));

        var first = dashboard.Calculate(a);

        Assert.Same(first, dashboard.Calculate(b));
        Assert.NotSame(first, dashboard.Calculate(a.With("Status", ValueSelection.Of("Open"))));
        Assert.Equal(2, dashboard.CachedStateCount);
        Assert.Equal(2, dashboard.CachedRowSetCount);
    }

    [Fact]
    public void Parallel_counting_gives_the_same_state()
    {
        var parallel = Build(b => b.EnableParallelCounting());
        var selections = Selections.Empty.With("Country", ValueSelection.Of("SE")).With("Amount", RangeSelection.AtLeast(100));

        var expected = Shared.Calculate(selections);
        var actual = parallel.Calculate(selections);

        Assert.True(parallel.ParallelCounting);
        Assert.Equal(expected.MatchingCount, actual.MatchingCount);
        foreach ((FacetState e, FacetState a) in expected.Facets.Zip(actual.Facets))
        {
            Assert.Equal(e.ContextCount, a.ContextCount);
            switch (e)
            {
                case ValueFacetState ev:
                    Assert.Equal(ev.Values, ((ValueFacetState)a).Values);
                    break;
                case RangeFacetState er:
                    Assert.Equal(er.Buckets, ((RangeFacetState)a).Buckets);
                    break;
                case DateFacetState ed:
                    Assert.Equal(ed.Buckets, ((DateFacetState)a).Buckets);
                    break;
            }
        }
    }

    [Fact]
    public void State_exposes_facets_and_metrics_in_definition_order()
    {
        var state = Shared.Calculate();

        Assert.Equal(["Country", "Status", "IsActive", "Amount", "Discount", "OrderDate"], state.Facets.Select(f => f.Key));
        Assert.Equal([FacetKind.Value, FacetKind.Value, FacetKind.Boolean, FacetKind.Range, FacetKind.Range, FacetKind.Date], state.Facets.Select(f => f.Kind));
        Assert.Equal(["orders", "revenue", "avgDiscount"], state.Metrics.Select(m => m.Key));
        Assert.True(state.TryGetFacet("Status", out FacetState status));
        Assert.Equal("Status", status.Title);
        Assert.False(status.HasSelection);
    }

    [Fact]
    public void Empty_dataset_calculates()
    {
        var empty = Dashboard.Create(Array.Empty<Order>(), b =>
        {
            b.ValueFacet(x => x.Country).Top(5);
            b.RangeFacet(x => x.Amount);
            b.DateFacet(x => x.OrderDate).Presets(DatePreset.Today);
            b.Count("orders");
            b.Sum("revenue", x => x.Amount);
        });

        var state = empty.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(0, state.MatchingCount);
        Assert.Empty(Values(state, "Country").Values);
        Assert.Null(Values(state, "Country").Other);
        Assert.Empty(((RangeFacetState)state.Facet("Amount")).Buckets);
        Assert.Single(((DateFacetState)state.Facet("OrderDate")).Presets);
        Assert.Equal(0, state.Metric("orders").Value);
        Assert.Null(state.Metric("revenue").Value);
        Assert.Empty(state.GetPage(0, 10).Items);
    }
}
