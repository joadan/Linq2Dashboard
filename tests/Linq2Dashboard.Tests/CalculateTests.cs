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
            b.CountMetric("orders");
            b.SumMetric("revenue", x => x.Amount);
            b.AverageMetric("avgDiscount", x => x.Discount);
            b.OrderByDescending(x => x.Amount);
            b.UseTimeProvider(Clock);
            extra?.Invoke(b);
        });

    private static readonly Dashboard<Order> Shared = Build();

    private static ValueFacetState Values(DashboardState<Order> state, string key) => (ValueFacetState)state.Facet(key);

    private static IEnumerable<DatePreset> Presets(DashboardState<Order> state, string key) =>
        ((DateFacetState)state.Facet(key)).Presets.Select(p => p.Preset);

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

    /// <summary>Concept §5: bars toggle like values. Two bucket clicks select both buckets' rows and light both bars, and the null value joins as one more part.</summary>
    [Fact]
    public void Range_buckets_toggle_like_values_and_light_every_selected_bucket()
    {
        var amount = (RangeFacetState)Shared.Calculate().Facet("Amount");

        var selections = Selections.Empty
            .ToggleInterval("Amount", amount.Buckets[0].ToInterval())
            .ToggleInterval("Amount", amount.Buckets[3].ToInterval());
        var state = Shared.Calculate(selections);
        var clicked = (RangeFacetState)state.Facet("Amount");

        Assert.Equal(4, state.MatchingCount);
        Assert.Equal([true, false, false, true], clicked.Buckets.Select(b => b.Selected));
        Assert.Equal([2, 2, 2, 2], clicked.Buckets.Select(b => b.FilteredCount)); // the facet's own selection is excluded from its own counts
        Assert.False(clicked.Null.Selected);

        // Clicking the first bar again leaves the last bucket alone; clicking that too clears the facet.
        var one = Shared.Calculate(selections.ToggleInterval("Amount", amount.Buckets[0].ToInterval()));
        Assert.Equal([false, false, false, true], ((RangeFacetState)one.Facet("Amount")).Buckets.Select(b => b.Selected));
        Assert.True(one.Selections.ToggleInterval("Amount", amount.Buckets[3].ToInterval()).IsEmpty);

        // The null value toggles beside the buckets: with a bucket it adds its rows, alone it is the null rows only.
        var discount = (RangeFacetState)Shared.Calculate().Facet("Discount");
        var withNull = Shared.Calculate(Selections.Empty.With("Discount", discount.Buckets[0].ToSelection().ToggleNull()));
        var withNullDiscount = (RangeFacetState)withNull.Facet("Discount");
        Assert.True(withNullDiscount.Null.Selected);
        Assert.True(withNullDiscount.Buckets[0].Selected);
        Assert.Equal(discount.Buckets[0].TotalCount + discount.Null.TotalCount, withNull.MatchingCount);
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

    /// <summary>Concept §5: period bars and presets toggle like values; every selected part lights its bar or pill.</summary>
    [Fact]
    public void Date_buckets_and_presets_toggle_like_values_and_light_every_selected_part()
    {
        var dates = (DateFacetState)Shared.Calculate().Facet("OrderDate");

        var selections = Selections.Empty
            .ToggleInterval("OrderDate", dates.Buckets[0].ToInterval())
            .ToggleInterval("OrderDate", dates.Buckets[3].ToInterval());
        var state = Shared.Calculate(selections);
        var two = (DateFacetState)state.Facet("OrderDate");

        Assert.Equal(3, state.MatchingCount); // January (1) and April (2)
        Assert.Equal([true, false, false, true], two.Buckets.Select(b => b.Selected));
        Assert.All(two.Presets, p => Assert.False(p.Selected));

        // A preset joins as a part of its own: "Last 7 days" lights, its bar (March) does not, since a preset covers only part of a month.
        var withPreset = (DateFacetState)Shared.Calculate(selections.ToggleInterval("OrderDate", dates.Presets[1].ToInterval())).Facet("OrderDate");
        Assert.Equal([true, false, false, true], withPreset.Buckets.Select(b => b.Selected));
        Assert.Equal([false, true], withPreset.Presets.Select(p => p.Selected));

        // The March bar as a part lights "This month" as before, beside the other parts.
        var withMarch = (DateFacetState)Shared.Calculate(selections.ToggleInterval("OrderDate", dates.Buckets[2].ToInterval())).Facet("OrderDate");
        Assert.Equal([true, false, true, true], withMarch.Buckets.Select(b => b.Selected));
        Assert.Equal([true, false], withMarch.Presets.Select(p => p.Selected));

        // Toggling a part off removes it alone; the last one clears the facet.
        Assert.Equal(dates.Buckets[3].ToSelection(), selections.ToggleInterval("OrderDate", dates.Buckets[0].ToInterval())["OrderDate"]);
        Assert.True(selections.ToggleInterval("OrderDate", dates.Buckets[0].ToInterval()).ToggleInterval("OrderDate", dates.Buckets[3].ToInterval()).IsEmpty);
    }

    [Fact]
    public void Empty_presets_are_left_out_only_when_the_facet_asks_for_it()
    {
        var dashboard = Build(b =>
        {
            b.DateFacet("Lean", x => x.OrderDate).TimeZone(TestData.Stockholm)
                .Presets(DatePreset.Today, DatePreset.ThisMonth).SkipEmptyPresets();
            b.DateFacet("Full", x => x.OrderDate).TimeZone(TestData.Stockholm)
                .Presets(DatePreset.Today, DatePreset.ThisMonth);
        });
        var state = dashboard.Calculate();

        // Today is Sunday 15 March and no order falls on it, so only March is offered.
        Assert.Equal([DatePreset.ThisMonth], Presets(state, "Lean"));

        // Off by default: the preset stays, with a total of zero (concept §4.3).
        var full = (DateFacetState)state.Facet("Full");
        Assert.Equal([DatePreset.Today, DatePreset.ThisMonth], full.Presets.Select(p => p.Preset));
        Assert.Equal([(0, 0), (3, 3)], full.Presets.Select(p => (p.TotalCount, p.FilteredCount)));
    }

    [Fact]
    public void A_selected_empty_preset_is_left_out_too_and_stays_clearable()
    {
        var dashboard = Build(b => b.DateFacet("Lean", x => x.OrderDate).TimeZone(TestData.Stockholm)
            .Presets(DatePreset.Today, DatePreset.ThisMonth).SkipEmptyPresets());
        var state = dashboard.Calculate(Selections.Empty.With("Lean", DateSelection.Relative(DatePreset.Today)));
        var lean = (DateFacetState)state.Facet("Lean");

        // Being selected does not bring it back, the way a selected value no row has does not come back.
        Assert.Equal([DatePreset.ThisMonth], lean.Presets.Select(p => p.Preset));
        Assert.All(lean.Presets, p => Assert.False(p.Selected));

        // The selection still applies and the facet still reports it, so clearing works.
        Assert.Equal(0, state.MatchingCount);
        Assert.True(lean.HasSelection);
        Assert.Equal(DateSelection.Relative(DatePreset.Today), lean.Selection);
    }

    [Fact]
    public void A_preset_a_scope_empties_is_left_out_of_that_scope_alone()
    {
        var dashboard = Build(b => b.DateFacet("Lean", x => x.OrderDate).TimeZone(TestData.Stockholm)
            .Presets(DatePreset.Last7Days, DatePreset.ThisMonth).SkipEmptyPresets());

        // Last7Days is 9 to 15 March, which holds only row 4, a DK order.
        Assert.Equal([DatePreset.Last7Days, DatePreset.ThisMonth], Presets(dashboard.Calculate(), "Lean"));

        var swedish = dashboard.ScopeTo(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Equal([DatePreset.ThisMonth], Presets(swedish.Calculate(), "Lean"));
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
            b.MinMetric("cheapest", x => x.Amount);
            b.MaxMetric("dearest", x => x.Amount);
        }).Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Null(minMax.Metric("cheapest").Share);
        Assert.Null(minMax.Metric("dearest").Share);
    }

    [Fact]
    public void A_count_metric_is_the_matching_row_count_and_its_share_of_the_total()
    {
        // The Blazor package has no matching-count component; it renders a Count metric instead, which
        // only works because the two agree everywhere, scopes included (concept §4.4, design §9.5).
        foreach (Selections selections in new[]
        {
            Selections.Empty,
            Selections.Empty.With("Country", ValueSelection.Of("SE")),
            Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")),
            Selections.Empty.With("Amount", RangeSelection.AtLeast(1_000_000)),
        })
        {
            var state = Shared.Calculate(selections);
            Assert.Equal(state.MatchingCount, state.Metric("orders").Value);
            Assert.Equal((double)state.MatchingCount / state.TotalCount, state.Metric("orders").Share);
        }

        var scoped = Build().ScopeTo(x => x.Country is "SE" or "NO").Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Equal(scoped.MatchingCount, scoped.Metric("orders").Value);
        Assert.Equal((double)scoped.MatchingCount / scoped.TotalCount, scoped.Metric("orders").Share);
    }

    [Fact]
    public void A_share_of_a_zero_total_is_undefined()
    {
        var empty = Dashboard.Create(Array.Empty<Order>(), b =>
        {
            b.CountMetric("orders");
            b.SumMetric("revenue", x => x.Amount);
        }).Calculate();
        Assert.Equal(0, empty.Metric("orders").Value);
        Assert.Null(empty.Metric("orders").Share);
        Assert.Null(empty.Metric("revenue").Share);

        var cancelling = Dashboard.Create([new Signed(1, 10), new Signed(2, -10)], b =>
        {
            b.ValueFacet(x => x.Id);
            b.SumMetric("net", x => x.Amount);
        }).Calculate(Selections.Empty.With("Id", ValueSelection.Of(1)));
        Assert.Equal(10, cancelling.Metric("net").Value);
        Assert.Null(cancelling.Metric("net").Share);
    }

    [Fact]
    public void Distinct_counts_different_non_null_values_among_the_matching_rows()
    {
        var dashboard = Build(b =>
        {
            b.DistinctMetric("countries", x => x.Country);
            b.DistinctMetric("statuses", x => x.Status);
        });

        var all = dashboard.Calculate();
        Assert.Equal(3, all.Metric("countries").Value); // SE, NO, DK; "se" is SE and null is skipped
        Assert.Equal(1.0, all.Metric("countries").Share);
        Assert.Equal(3, all.Metric("statuses").Value);

        var se = dashboard.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Equal(1, se.Metric("countries").Value); // metrics follow every selection, the facet's own included
        Assert.Equal(1 / 3.0, se.Metric("countries").Share);
        Assert.Equal(2, se.Metric("statuses").Value); // Open, Closed
        Assert.Equal(2 / 3.0, se.Metric("statuses").Share);

        var noDiscounts = dashboard.Calculate(Selections.Empty.With("Discount", RangeSelection.OnlyNull));
        Assert.Equal(2, noDiscounts.Metric("countries").Value); // NO, DK; the null country does not count

        var none = dashboard.Calculate(Selections.Empty.With("Country", ValueSelection.Of("FI")));
        Assert.Null(none.Metric("countries").Value);
        Assert.Null(none.Metric("countries").Share);
    }

    [Fact]
    public void Distinct_takes_a_comparer_and_reports_no_value_over_only_nulls()
    {
        var caseSensitive = Build(b => b.DistinctMetric("spellings", x => x.Country, StringComparer.Ordinal)).Calculate();
        Assert.Equal(4, caseSensitive.Metric("spellings").Value); // SE, NO, DK, se

        var untagged = Dashboard.Create([new Tagged(1, null), new Tagged(2, null)], b => b.DistinctMetric("tags", x => x.Tag)).Calculate();
        Assert.Equal(new MetricState("tags", "tags", Aggregation.Distinct, null, null), untagged.Metric("tags"));
    }

    [Fact]
    public void Calculated_metrics_derive_from_the_metrics_defined_before_them()
    {
        var dashboard = Build(b =>
        {
            b.CalculatedMetric("aov", m => m["revenue"] / m["orders"]).Name("Average order");
            b.CalculatedMetric("aovShare", m => m.Share("revenue") / m.Share("orders")); // reads shares, and an earlier calculated metric is visible too
            b.CalculatedMetric("doubleAov", m => m["aov"] * 2);
        });

        var all = dashboard.Calculate();
        Assert.Equal(new MetricState("aov", "Average order", Aggregation.Calculated, 5424.5 / 8, null), all.Metric("aov"));
        Assert.Equal(1.0, all.Metric("aovShare").Value);
        Assert.Equal(5424.5 / 4, all.Metric("doubleAov").Value);

        var se = dashboard.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Equal(3599.5 / 3, se.Metric("aov").Value);
        Assert.Null(se.Metric("aov").Share); // a ratio is not a part of anything

        var none = dashboard.Calculate(Selections.Empty.With("Country", ValueSelection.Of("FI")));
        Assert.Null(none.Metric("aov").Value); // revenue has no value, so neither has the formula
        Assert.Null(none.Metric("doubleAov").Value);
    }

    [Fact]
    public void A_calculated_metric_that_is_not_finite_has_no_value()
    {
        var state = Build(b =>
        {
            b.CalculatedMetric("byZero", m => m["revenue"] / (m["orders"] - 8));
            b.CalculatedMetric("nan", m => double.NaN);
        }).Calculate();

        Assert.Null(state.Metric("byZero").Value); // 5 424.5 / 0 is "no value", never infinity
        Assert.False(state.Metric("nan").HasValue);
    }

    private sealed record Signed(int Id, decimal Amount);

    private sealed record Tagged(int Id, string? Tag);

    [Fact]
    public void Items_follow_the_application_order()
    {
        var state = Shared.Calculate();

        Assert.Equal(8, state.Items.Count);
        Assert.Equal([6, 5, 4, 3, 2, 1, 8, 7], state.Items.Select(o => o.Id));
        Assert.Equal(6, state.Items[0].Id);
        Assert.Equal(7, state.Items[7].Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Items[8]);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Items[-1]);
    }

    [Fact]
    public void Items_contain_only_matching_rows()
    {
        var state = Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(3, state.Items.Count);
        Assert.Equal([6, 4, 1], state.Items.Select(o => o.Id));
        Assert.Equal(1, state.Items[2].Id);
    }

    [Fact]
    public void Without_a_sort_order_items_follow_row_order()
    {
        var unsorted = Dashboard.Create(TestData.Orders(), b => b.ValueFacet(x => x.Country));

        var state = unsorted.Calculate(Selections.Empty.With("Country", ValueSelection.Of("NO", null)));

        Assert.Equal([2, 3, 7, 8], state.Items.Select(o => o.Id));
        Assert.Equal(7, state.Items[2].Id);
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
        Assert.Equal(state.Items.Skip(3).Take(3), state.GetItems(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetItems(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetItems(0, -1));
    }

    /// <summary>
    /// A data grid pages, virtualises, sorts and counts through LINQ, often over <c>AsQueryable()</c>;
    /// the list fast paths it relies on need <c>IList&lt;T&gt;</c>, and the list must refuse writes (design §4.7).
    /// </summary>
    [Fact]
    public void Items_is_a_read_only_list_that_LINQ_and_a_grid_can_index()
    {
        var state = Shared.Calculate();

        var list = Assert.IsAssignableFrom<IList<Order>>(state.Items);
        Assert.True(list.IsReadOnly);
        Assert.Equal(8, list.Count);
        Assert.Equal(3, list.IndexOf(state.Items[3]));
        Assert.Contains(state.Items[5], list);
        Assert.Throws<NotSupportedException>(() => list.Add(state.Items[0]));
        Assert.Throws<NotSupportedException>(() => list[0] = state.Items[1]);
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(list.Clear);

        var copy = new Order[10];
        list.CopyTo(copy, 2);
        Assert.Equal(state.Items, copy.Skip(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => list.CopyTo(new Order[7], 0));

        IQueryable<Order> query = state.Items.AsQueryable();
        Assert.Equal(8, query.Count());
        Assert.Equal([3, 2], query.Skip(3).Take(2).Select(o => o.Id));
        Assert.Equal([1, 2, 3], query.OrderBy(o => o.Id).Skip(0).Take(3).Select(o => o.Id));
        Assert.Equal([5, 4], query.OrderByDescending(o => o.Id).Skip(3).Take(2).Select(o => o.Id));
    }

    [Fact]
    public void Items_are_empty_when_nothing_matches()
    {
        var state = Shared.Calculate(Selections.Empty.With("Country", ValueSelection.Of("XX")));

        Assert.Empty(state.Items.AsQueryable());
        Assert.Empty(state.Items);
        Assert.Empty(state.GetItems(0, 10));
        Assert.Equal(0, state.Items.AsQueryable().Count());
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
    public void Labels_come_from_the_first_row_that_introduces_the_value()
    {
        // Country "SE" is introduced by row 0 (Stockholm); row 5 spells it "se" and lives in Malmö, but the first row wins (concept §5).
        var dashboard = Build(b => b.ValueFacet("city", x => x.Country).Label(x => x.Address?.City));
        var facet = Values(dashboard.Calculate(), "city");

        Assert.Equal([("SE", "Stockholm"), ("NO", "Oslo"), (null, null), ("DK", "Copenhagen")], facet.Values.Select(v => (v.Value, v.Label)));
        Assert.Equal("Stockholm", facet.LabelOf("SE"));
        Assert.Equal("Stockholm", facet.LabelOf("se")); // the facet's comparer applies
        Assert.Null(facet.LabelOf(null));
        Assert.Null(facet.LabelOf("XX")); // a value that does not occur
    }

    [Fact]
    public void Search_matches_the_label_when_one_is_defined()
    {
        var dashboard = Build(b => b.ValueFacet("city", x => x.Country).Label(x => x.Address?.City).Searchable());
        var facet = Values(dashboard.Calculate(), "city");

        Assert.Equal([("SE", 3, 3, false)], Flatten(facet.Search("stock")));
        Assert.Equal(["Stockholm"], facet.Search("stock").Select(v => v.Label));
        Assert.Empty(facet.Search("SE")); // the value's own text is no longer what search sees
    }

    [Fact]
    public void A_null_label_falls_back_to_the_value()
    {
        // Rows 2 and 6 (ids 3 and 7) have no address, so their labels are null and search sees the id.
        var dashboard = Build(b => b.ValueFacet("id", x => x.Id).Label(x => x.Address?.City));
        var facet = Values(dashboard.Calculate(), "id");

        Assert.Null(facet.Values.Single(v => Equals(v.Value, 3)).Label);
        Assert.Equal("Oslo", facet.Values.Single(v => Equals(v.Value, 2)).Label);
        Assert.Null(facet.LabelOf(3));
        Assert.Equal("Oslo", facet.LabelOf(2L)); // the same conversions as a selection value
        Assert.Equal([3], facet.Search("3").Select(v => v.Value));
        Assert.Equal([2], facet.Search("oslo").Select(v => v.Value));
    }

    [Fact]
    public void Without_a_label_selector_there_are_no_labels()
    {
        var facet = Values(Shared.Calculate(), "Country");

        Assert.All(facet.Values, v => Assert.Null(v.Label));
        Assert.Null(facet.LabelOf("SE"));
    }

    [Fact]
    public void Selections_and_json_use_the_value_not_the_label()
    {
        var dashboard = Build(b => b.ValueFacet("city", x => x.Country).Label(x => x.Address?.City));
        var selections = Selections.Empty.With("city", ValueSelection.Of("SE"));

        var state = dashboard.Calculate(selections);
        Assert.Equal(3, state.MatchingCount);
        Assert.Contains("\"SE\"", dashboard.Serializer.ToJson(selections));
        Assert.DoesNotContain("Stockholm", dashboard.Serializer.ToJson(selections));
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
        Assert.Equal("Status", status.Name);
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
            b.CountMetric("orders");
            b.SumMetric("revenue", x => x.Amount);
        });

        var state = empty.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(0, state.MatchingCount);
        Assert.Empty(Values(state, "Country").Values);
        Assert.Null(Values(state, "Country").Other);
        Assert.Empty(((RangeFacetState)state.Facet("Amount")).Buckets);
        Assert.Single(((DateFacetState)state.Facet("OrderDate")).Presets);
        Assert.Equal(0, state.Metric("orders").Value);
        Assert.Null(state.Metric("revenue").Value);
        Assert.Empty(state.Items);
    }
}
