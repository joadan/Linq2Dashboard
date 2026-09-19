using Linq2Dashboard.Facets;

namespace Linq2Dashboard.Tests;

public class FacetIndexTests
{
    private static readonly FixedTimeProvider Clock = new(TestData.Instant("2026-03-15T10:00:00Z")); // Sunday 15 March, 11:00 in Stockholm

    private static readonly Dashboard<Order> Dashboard = Linq2Dashboard.Dashboard.Create(TestData.Orders(), b =>
    {
        b.ValueFacet(x => x.Country);
        b.ValueFacet(x => x.Quantity);
        b.ValueFacet(x => x.Kind);
        b.BooleanFacet(x => x.IsActive);
        b.BooleanFacet(x => x.Verified);
        b.RangeFacet(x => x.Discount).Buckets(10, 100);
        b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm).Presets(DatePreset.Last7Days, DatePreset.ThisMonth);
        b.DateFacet(x => x.Shipped);
        b.UseTimeProvider(Clock);
    });

    private static IEnumerable<int> Rows(string facet, Selection selection) =>
        Dashboard.FacetIndex(facet).RowsMatching(selection).Rows();

    [Fact]
    public void Value_selection_matches_any_selected_value_case_insensitively()
    {
        Assert.Equal([0, 3, 5], Rows("Country", ValueSelection.Of("SE")));
        Assert.Equal([0, 3, 5], Rows("Country", ValueSelection.Of("se")));
        Assert.Equal([0, 1, 3, 5, 7], Rows("Country", ValueSelection.Of("SE", "no")));
    }

    [Fact]
    public void Value_selection_with_null_selects_the_null_rows()
    {
        Assert.Equal([2, 6], Rows("Country", ValueSelection.Of(null)));
        Assert.Equal([2, 4, 6], Rows("Country", ValueSelection.Of(null, "DK")));
    }

    [Fact]
    public void Value_not_in_the_dataset_matches_nothing_without_failing()
    {
        Assert.Empty(Rows("Country", ValueSelection.Of("FI")));
        Assert.Equal([4], Rows("Country", ValueSelection.Of("FI", "DK")));
    }

    [Fact]
    public void Nullable_struct_facet_accepts_convertible_values()
    {
        Assert.Equal([0, 7], Rows("Quantity", ValueSelection.Of(2)));
        Assert.Equal([0, 7], Rows("Quantity", ValueSelection.Of(2L)));
        Assert.Equal([0, 7], Rows("Quantity", ValueSelection.Of("2")));
        Assert.Equal([0, 7], Rows("Quantity", ValueSelection.Of(2.0)));
        Assert.Equal([1, 5], Rows("Quantity", ValueSelection.Of(null)));
    }

    [Fact]
    public void Enum_facet_accepts_enum_name_and_number()
    {
        Assert.Equal([1, 4, 6], Rows("Kind", ValueSelection.Of(OrderKind.Store)));
        Assert.Equal([1, 4, 6], Rows("Kind", ValueSelection.Of("store")));
        Assert.Equal([1, 4, 6], Rows("Kind", ValueSelection.Of(1)));
    }

    [Fact]
    public void Unconvertible_value_is_an_error()
    {
        var error = Assert.Throws<ArgumentException>(() => Rows("Quantity", ValueSelection.Of(new object())));
        Assert.Contains("Quantity", error.Message);
        Assert.Throws<ArgumentException>(() => Rows("Quantity", ValueSelection.Of("two")));
        Assert.Throws<ArgumentException>(() => Rows("Kind", ValueSelection.Of("Mail")));
    }

    [Fact]
    public void Boolean_facets_select_true_false_and_null()
    {
        Assert.Equal([0, 1, 3, 5, 7], Rows("IsActive", ValueSelection.Of(true)));
        Assert.Equal([2, 4, 6], Rows("IsActive", ValueSelection.Of(false)));
        Assert.Equal([1, 4], Rows("Verified", ValueSelection.Of(null)));
        Assert.Equal([1, 2, 4, 5], Rows("Verified", ValueSelection.Of(false, null)));
    }

    [Fact]
    public void Range_selection_uses_the_interval_flags()
    {
        // Discount: 10, null, 50, 0, null, 250, null, 5
        Assert.Equal([0, 2, 7], Rows("Discount", RangeSelection.Between(5, 50)));
        Assert.Equal([0, 7], Rows("Discount", new RangeSelection(5, 50, toInclusive: false)));
        Assert.Equal([0, 2, 5], Rows("Discount", RangeSelection.AtLeast(10)));
        Assert.Equal([0, 1, 3, 4, 6, 7], Rows("Discount", new RangeSelection(null, 10, includeNull: true)));
    }

    [Fact]
    public void Range_only_null_selects_exactly_the_null_rows()
    {
        Assert.Equal([1, 4, 6], Rows("Discount", RangeSelection.OnlyNull));
    }

    /// <summary>Concept §4.1, §5: the intervals of one range selection combine as OR, with the null rows as one more part.</summary>
    [Fact]
    public void Range_intervals_combine_as_or_within_the_facet()
    {
        // Discount: 10, null, 50, 0, null, 250, null, 5
        var low = new RangeInterval(null, 10, toInclusive: false);   // 0, 5
        var high = RangeInterval.AtLeast(50);                         // 50, 250
        var overlapping = RangeInterval.Between(5, 50);               // 10, 50, 5

        Assert.Equal([2, 3, 5, 7], Rows("Discount", new RangeSelection([low, high])));
        Assert.Equal([2, 3, 5, 7], Rows("Discount", new RangeSelection([high, low])));
        Assert.Equal([0, 2, 3, 5, 7], Rows("Discount", new RangeSelection([low, overlapping, high])));   // a row in two intervals counts once
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], Rows("Discount", new RangeSelection([low, high], includeNull: true)));
        Assert.Empty(Rows("Discount", RangeSelection.Empty));
    }

    [Fact]
    public void Date_parts_combine_as_or_and_a_preset_mixes_with_an_interval()
    {
        // Rows 0 to 2 are before March, 3 to 5 in March, 6 and 7 after; Last7Days as of 15 March is row 4 alone.
        var before = DateInterval.Between(null, TestData.Instant("2026-03-01T00:00:00+01:00"));
        var after = DateInterval.Between(TestData.Instant("2026-04-01T00:00:00+02:00"), null);
        var recent = DateInterval.Relative(DatePreset.Last7Days);

        Assert.Equal([0, 1, 2, 6, 7], Rows("OrderDate", new DateSelection([before, after])));
        Assert.Equal([0, 1, 2, 4], Rows("OrderDate", new DateSelection([recent, before])));
        Assert.Equal([0, 1, 2, 4, 6, 7], Rows("OrderDate", new DateSelection([before, recent, after])));
        Assert.Empty(Rows("OrderDate", DateSelection.Empty));
    }

    [Fact]
    public void Absolute_date_selection_is_half_open_on_instants()
    {
        var from = TestData.Instant("2026-03-01T00:00:00+01:00");
        var to = TestData.Instant("2026-04-01T00:00:00+02:00");

        // March in Stockholm: rows 3 (1 Mar 23:30), 4 (10 Mar), 5 (31 Mar 22:30). Row 6 is 2 April.
        Assert.Equal([3, 4, 5], Rows("OrderDate", DateSelection.Between(from, to)));
        Assert.Equal([3, 4, 5, 6, 7], Rows("OrderDate", DateSelection.Between(from, null)));
        Assert.Equal([0, 1, 2], Rows("OrderDate", DateSelection.Between(null, from)));
    }

    [Fact]
    public void Relative_date_selection_resolves_against_the_time_provider_in_the_facet_zone()
    {
        // Now is Sunday 15 March 11:00 Stockholm. Last7Days = [9 Mar, 16 Mar). ThisMonth = [1 Mar, 1 Apr).
        Assert.Equal([4], Rows("OrderDate", DateSelection.Relative(DatePreset.Last7Days)));
        Assert.Equal([3, 4, 5], Rows("OrderDate", DateSelection.Relative(DatePreset.ThisMonth)));
        Assert.Empty(Rows("OrderDate", DateSelection.Relative(DatePreset.Today)));

        var index = Assert.IsType<DateFacetIndex>(Dashboard.FacetIndex("OrderDate"));
        Assert.Equal(
            (TestData.Instant("2026-03-09T00:00:00+01:00"), TestData.Instant("2026-03-16T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.Last7Days));
        Assert.Equal(
            (TestData.Instant("2026-03-09T00:00:00+01:00"), TestData.Instant("2026-03-16T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.ThisWeek));
        Assert.Equal(
            (TestData.Instant("2026-02-14T00:00:00+01:00"), TestData.Instant("2026-03-16T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.Last30Days));
        Assert.Equal(
            (TestData.Instant("2026-03-14T00:00:00+01:00"), TestData.Instant("2026-03-15T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.Yesterday));
        Assert.Equal(
            (TestData.Instant("2026-01-01T00:00:00+01:00"), TestData.Instant("2027-01-01T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.ThisYear));
    }

    [Fact]
    public void This_and_last_period_presets_are_adjacent_calendar_periods()
    {
        // Now is Sunday 15 March 2026. LastWeek = 2-8 March, LastMonth = February, LastYear = the whole of 2025.
        Assert.Empty(Rows("OrderDate", DateSelection.Relative(DatePreset.LastWeek)));
        Assert.Equal([1, 2], Rows("OrderDate", DateSelection.Relative(DatePreset.LastMonth)));
        Assert.Empty(Rows("OrderDate", DateSelection.Relative(DatePreset.LastYear)));

        var index = Assert.IsType<DateFacetIndex>(Dashboard.FacetIndex("OrderDate"));
        Assert.Equal(
            (TestData.Instant("2026-03-02T00:00:00+01:00"), TestData.Instant("2026-03-09T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.LastWeek));
        Assert.Equal(
            (TestData.Instant("2026-02-01T00:00:00+01:00"), TestData.Instant("2026-03-01T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.LastMonth));
        Assert.Equal(
            (TestData.Instant("2025-01-01T00:00:00+01:00"), TestData.Instant("2026-01-01T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.LastYear));

        // Each ends exactly where its "this" counterpart begins.
        Assert.Equal(index.ResolvePreset(DatePreset.ThisWeek).From, index.ResolvePreset(DatePreset.LastWeek).To);
        Assert.Equal(index.ResolvePreset(DatePreset.ThisMonth).From, index.ResolvePreset(DatePreset.LastMonth).To);
        Assert.Equal(index.ResolvePreset(DatePreset.ThisYear).From, index.ResolvePreset(DatePreset.LastYear).To);
    }

    [Fact]
    public void Year_to_date_ends_with_today_rather_than_with_the_year()
    {
        // Now is Sunday 15 March 2026. YearToDate = [1 Jan, 16 Mar), so the later rows of 2026 fall outside it.
        Assert.Equal([0, 1, 2, 3, 4], Rows("OrderDate", DateSelection.Relative(DatePreset.YearToDate)));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], Rows("OrderDate", DateSelection.Relative(DatePreset.ThisYear)));

        var index = Assert.IsType<DateFacetIndex>(Dashboard.FacetIndex("OrderDate"));
        Assert.Equal(
            (TestData.Instant("2026-01-01T00:00:00+01:00"), TestData.Instant("2026-03-16T00:00:00+01:00")),
            index.ResolvePreset(DatePreset.YearToDate));

        // It starts with the year and ends with today.
        Assert.Equal(index.ResolvePreset(DatePreset.ThisYear).From, index.ResolvePreset(DatePreset.YearToDate).From);
        Assert.Equal(index.ResolvePreset(DatePreset.Today).To, index.ResolvePreset(DatePreset.YearToDate).To);
    }

    [Fact]
    public void Last_month_across_a_dst_change_carries_both_offsets()
    {
        var clock = new FixedTimeProvider(TestData.Instant("2026-04-15T10:00:00Z"));
        var (from, to) = DatePresets.Resolve(DatePreset.LastMonth, clock.GetUtcNow(), TestData.Stockholm);

        Assert.Equal(TestData.Instant("2026-03-01T00:00:00+01:00"), from);
        Assert.Equal(TestData.Instant("2026-04-01T00:00:00+02:00"), to);
    }

    [Fact]
    public void Preset_across_a_dst_change_carries_both_offsets()
    {
        var clock = new FixedTimeProvider(TestData.Instant("2026-03-30T10:00:00Z")); // Monday after DST start
        var (from, to) = DatePresets.Resolve(DatePreset.Last7Days, clock.GetUtcNow(), TestData.Stockholm);

        Assert.Equal(TestData.Instant("2026-03-24T00:00:00+01:00"), from);
        Assert.Equal(TestData.Instant("2026-03-31T00:00:00+02:00"), to);
    }

    [Fact]
    public void Date_null_handling()
    {
        Assert.Equal([1, 4, 6], Rows("Shipped", DateSelection.OnlyNull));
        Assert.Equal([0, 1, 2, 4, 6], Rows("Shipped", DateSelection.Between(null, TestData.Instant("2026-03-01T00:00:00Z")) with { IncludeNull = true }));
    }

    [Fact]
    public void Selection_of_the_wrong_kind_is_rejected_with_the_facet_named()
    {
        var error = Assert.Throws<ArgumentException>(() => Rows("Country", RangeSelection.Between(0, 1)));
        Assert.Contains("Country", error.Message);
        Assert.Contains("ValueSelection", error.Message);
        Assert.Throws<ArgumentException>(() => Rows("Discount", ValueSelection.Of(1)));
        Assert.Throws<ArgumentException>(() => Rows("OrderDate", RangeSelection.Between(0, 1)));
        Assert.Throws<ArgumentNullException>(() => Rows("Country", null!));
    }
}
