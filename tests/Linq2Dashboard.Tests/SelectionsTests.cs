namespace Linq2Dashboard.Tests;

public class SelectionsTests
{
    [Fact]
    public void ValueSelection_deduplicates_and_compares_as_a_set()
    {
        var a = ValueSelection.Of("SE", "NO", "SE");
        var b = ValueSelection.Of("NO", "SE");

        Assert.Equal(2, a.Values.Count);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, ValueSelection.Of("SE"));
        Assert.NotEqual(a, ValueSelection.Of("SE", "DK"));
    }

    [Fact]
    public void ValueSelection_treats_null_as_a_value()
    {
        var selection = ValueSelection.Of("SE", null);

        Assert.True(selection.Contains(null));
        Assert.Equal(selection, ValueSelection.Of(null, "SE"));
        Assert.Equal(ValueSelection.Of("SE"), selection.Remove(null));
        Assert.Equal(selection, ValueSelection.Of("SE").Add(null));
    }

    [Fact]
    public void ValueSelection_add_and_remove_are_idempotent()
    {
        var selection = ValueSelection.Of("SE");

        Assert.Same(selection, selection.Add("SE"));
        Assert.Same(selection, selection.Remove("NO"));
        Assert.True(selection.Remove("SE").IsEmpty);
    }

    [Fact]
    public void RangeSelection_validates_bounds()
    {
        Assert.Throws<ArgumentException>(() => RangeSelection.Between(500, 100));
        Assert.Throws<ArgumentException>(() => new RangeSelection(double.NaN, 1));
        Assert.Throws<ArgumentException>(() => new RangeSelection(1, double.NaN));
        Assert.Throws<ArgumentException>(() => RangeInterval.Between(500, 100));

        var closed = RangeSelection.Between(100, 500);
        RangeInterval interval = Assert.Single(closed.Intervals);
        Assert.Equal((100, 500, true, true, false), (interval.From, interval.To, interval.FromInclusive, interval.ToInclusive, closed.IncludeNull));
        Assert.Null(RangeSelection.AtLeast(5).Intervals[0].To);
        Assert.Null(RangeSelection.AtMost(5).Intervals[0].From);
        Assert.True(RangeSelection.OnlyNull.OnlyNulls);
        Assert.True(RangeSelection.Empty.IsEmpty);
        Assert.False(RangeSelection.OnlyNull.IsEmpty);
        Assert.Equal(closed, closed with { });
        Assert.NotEqual(closed, new RangeSelection(100, 500, toInclusive: false));
        Assert.NotEqual(closed, closed with { IncludeNull = true });
    }

    /// <summary>Concept §5: a range selection is a set of intervals, combined as OR, toggled one at a time like values.</summary>
    [Fact]
    public void RangeSelection_is_a_set_of_intervals()
    {
        var low = new RangeInterval(null, 100, toInclusive: false);
        var high = RangeInterval.AtLeast(1000);

        var one = RangeSelection.Empty.Toggle(low);
        var two = one.Toggle(high);
        var backToOne = two.Toggle(low);

        Assert.Equal([low], one.Intervals);
        Assert.Equal(2, two.Intervals.Count);
        Assert.True(two.Contains(low));
        Assert.True(two.Contains(high));
        Assert.Equal([high], backToOne.Intervals);
        Assert.True(backToOne.Toggle(high).IsEmpty);

        Assert.Equal(two, new RangeSelection([high, low]));                       // order does not matter
        Assert.Equal(two.GetHashCode(), new RangeSelection([high, low]).GetHashCode());
        Assert.Equal(two, new RangeSelection([low, high, low]));                  // duplicates dropped
        Assert.Same(two, two.Add(high));
        Assert.Same(two, two.Remove(RangeInterval.Between(200, 300)));           // nothing to carve out
        Assert.NotEqual(two, two.ToggleNull());
        Assert.True(two.ToggleNull().IncludeNull);
        Assert.Equal(two, two.ToggleNull().ToggleNull());
        Assert.True(RangeSelection.Empty.ToggleNull().OnlyNulls);
        Assert.True(RangeSelection.OnlyNull.ToggleNull().IsEmpty);
        Assert.Throws<ArgumentException>(() => new RangeSelection([low, null!]));
    }

    /// <summary>Concept §5: adjacent or overlapping intervals are one interval, so two neighbouring bars span one range and the set is canonical.</summary>
    [Fact]
    public void RangeSelection_joins_adjacent_and_overlapping_intervals()
    {
        var first = new RangeInterval(100, 200, toInclusive: false);
        var second = new RangeInterval(200, 300, toInclusive: false);
        var third = new RangeInterval(300, 400, toInclusive: false);
        var joined = new RangeInterval(100, 300, toInclusive: false);

        Assert.Equal([joined], RangeSelection.Empty.Toggle(first).Toggle(second).Intervals);
        Assert.Equal([joined], new RangeSelection([second, first]).Intervals);                         // canonical whatever the order
        Assert.Equal(new RangeSelection([joined]), new RangeSelection([first, second]));
        Assert.Equal(new RangeSelection([joined]).GetHashCode(), new RangeSelection([first, second]).GetHashCode());
        Assert.Equal([new RangeInterval(100, 400, toInclusive: false)], new RangeSelection([third, first, second]).Intervals);

        Assert.Equal([RangeInterval.Between(100, 350)], new RangeSelection([RangeInterval.Between(150, 350), first]).Intervals);   // overlap
        Assert.Equal([new RangeInterval(100, 300, toInclusive: false)], new RangeSelection([first, RangeInterval.Between(150, 250), second]).Intervals);
        Assert.Equal([RangeInterval.Between(100, 200)], new RangeSelection([first, RangeInterval.Between(200, 200)]).Intervals);   // a point closes the open end

        Assert.Equal(2, new RangeSelection([first, third]).Intervals.Count);                              // a gap keeps them apart
        Assert.Equal([first, third], new RangeSelection([third, first]).Intervals);                      // ascending
        Assert.Equal(2, new RangeSelection([first, new RangeInterval(200, 300, fromInclusive: false)]).Intervals.Count);   // [100,200) and (200,300) exclude 200

        Assert.Equal([new RangeInterval(null, 200, toInclusive: false)], new RangeSelection([new RangeInterval(null, 100, toInclusive: false), first]).Intervals);   // open tail
        Assert.Equal([RangeInterval.AtLeast(300)], new RangeSelection([third, RangeInterval.AtLeast(400)]).Intervals);
        Assert.Equal([new RangeInterval(null, null)], new RangeSelection([RangeInterval.AtMost(5), RangeInterval.AtLeast(3)]).Intervals);
    }

    /// <summary>Concept §5: a click on a covered bar carves its interval out, splitting the interval it sits inside, so the outer bars stay selected.</summary>
    [Fact]
    public void RangeSelection_toggle_carves_a_covered_interval_out()
    {
        var first = new RangeInterval(100, 200, toInclusive: false);
        var second = new RangeInterval(200, 300, toInclusive: false);
        var third = new RangeInterval(300, 400, toInclusive: false);
        var all = new RangeSelection([first, second, third]);

        Assert.True(all.Contains(second));
        Assert.True(all.Contains(RangeInterval.Between(150, 350)));
        Assert.False(all.Contains(RangeInterval.Between(150, 400)));                                    // 400 is not in [100,400)
        Assert.False(all.Contains(RangeInterval.AtLeast(300)));

        Assert.Equal([first, third], all.Toggle(second).Intervals);                                      // the middle bar goes, two remain
        Assert.Equal([new RangeInterval(200, 400, toInclusive: false)], all.Toggle(first).Intervals);   // an end bar
        Assert.Equal([new RangeInterval(100, 300, toInclusive: false)], all.Toggle(third).Intervals);
        Assert.True(all.Toggle(first).Toggle(second).Toggle(third).IsEmpty);                            // one by one back to nothing
        Assert.Equal(all, all.Toggle(second).Toggle(second));                                          // out and in again
        Assert.Same(all, all.Add(second));

        // Carving out of a closed slider interval leaves the right ends: [150,200) and [300,350].
        var slider = RangeSelection.Between(150, 350);
        Assert.Equal([new RangeInterval(150, 200, toInclusive: false), new RangeInterval(300, 350, fromInclusive: true)], slider.Toggle(second).Intervals);
        Assert.Equal([RangeInterval.Between(150, 200), new RangeInterval(300, 350, fromInclusive: false)], slider.Remove(new RangeInterval(200, 300, fromInclusive: false)).Intervals);

        // A bar the slider covers only in part is not selected, so a click adds it and the union grows.
        Assert.False(slider.Contains(third));
        Assert.Equal([new RangeInterval(150, 400, toInclusive: false)], slider.Toggle(third).Intervals);

        // Remove is set difference even when the interval is not fully covered.
        Assert.Equal([new RangeInterval(100, 150, toInclusive: false), new RangeInterval(350, 400, fromInclusive: false, toInclusive: false)], all.Remove(RangeInterval.Between(150, 350)).Intervals);
        Assert.Equal([RangeInterval.Between(400, 400)], new RangeSelection([RangeInterval.Between(100, 400)]).Remove(new RangeInterval(null, 400, toInclusive: false)).Intervals);
        Assert.True(all.Remove(new RangeInterval(null, null)).IsEmpty);
        Assert.False(all.ToggleNull().Remove(new RangeInterval(null, null)).IsEmpty);                   // the null rows stay
    }

    /// <summary>Concept §5: adjacent months join into one interval, while a preset stays a part of its own and never merges.</summary>
    [Fact]
    public void DateSelection_joins_adjacent_intervals_but_not_presets()
    {
        var january = DateInterval.Between(TestData.Instant("2026-01-01T00:00:00Z"), TestData.Instant("2026-02-01T00:00:00Z"));
        var february = DateInterval.Between(TestData.Instant("2026-02-01T00:00:00Z"), TestData.Instant("2026-03-01T00:00:00Z"));
        var march = DateInterval.Between(TestData.Instant("2026-03-01T00:00:00Z"), TestData.Instant("2026-04-01T00:00:00Z"));
        var januaryToFebruary = DateInterval.Between(january.From, february.To);
        var recent = DateInterval.Relative(DatePreset.Last7Days);
        var today = DateInterval.Relative(DatePreset.Today);

        Assert.Equal([januaryToFebruary], DateSelection.Empty.Toggle(january).Toggle(february).Intervals);
        Assert.Equal([januaryToFebruary], new DateSelection([february, january]).Intervals);
        Assert.Equal(new DateSelection([januaryToFebruary]), new DateSelection([january, february]));
        Assert.Equal([january, march], new DateSelection([march, january]).Intervals);                  // a gap keeps them apart, ascending
        Assert.Equal([DateInterval.Between(january.From, march.To)], new DateSelection([january, DateInterval.Between(february.From!.Value.AddDays(-10), march.From), march]).Intervals);   // overlap
        Assert.Equal([DateInterval.Between(null, february.To)], new DateSelection([DateInterval.Between(null, january.To), february]).Intervals);   // open tail

        var withPresets = new DateSelection([recent, february, today, january]);
        Assert.Equal([januaryToFebruary, today, recent], withPresets.Intervals);                        // intervals first, then presets
        Assert.True(withPresets.Contains(recent));
        Assert.False(new DateSelection([DateInterval.Between(null, null)]).Contains(recent));            // an unbounded interval does not stand in for a preset
        Assert.Equal([januaryToFebruary, today], withPresets.Toggle(recent).Intervals);
        Assert.Equal(withPresets, withPresets.Toggle(recent).Toggle(recent));

        // Carving out: the middle month leaves the two around it; a preset is untouched by an interval.
        var quarter = new DateSelection([january, february, march, recent]);
        Assert.Equal([DateInterval.Between(january.From, march.To), recent], quarter.Intervals);
        Assert.True(quarter.Contains(february));
        Assert.Equal([january, march, recent], quarter.Toggle(february).Intervals);
        Assert.Equal([DateInterval.Between(february.From, march.To), recent], quarter.Toggle(january).Intervals);
        Assert.Equal(quarter, quarter.Toggle(february).Toggle(february));
        Assert.Equal([recent], quarter.Remove(DateInterval.Between(null, null)).Intervals);
        Assert.Same(quarter, quarter.Remove(DateInterval.Between(march.To, null)));
        Assert.False(quarter.Contains(DateInterval.Between(january.From, march.To!.Value.AddDays(1))));
    }

    [Fact]
    public void DateSelection_parts_are_absolute_or_relative()
    {
        var from = TestData.Instant("2026-03-01T00:00:00Z");
        var to = TestData.Instant("2026-04-01T00:00:00Z");

        var absolute = DateSelection.Between(from, to);
        var relative = DateSelection.Relative(DatePreset.Last7Days);

        DateInterval absolutePart = Assert.Single(absolute.Intervals);
        DateInterval relativePart = Assert.Single(relative.Intervals);
        Assert.False(absolutePart.IsRelative);
        Assert.Equal((from, to), (absolutePart.From, absolutePart.To));
        Assert.True(relativePart.IsRelative);
        Assert.Equal(DatePreset.Last7Days, relativePart.Preset);
        Assert.Null(relativePart.From);
        Assert.True(DateSelection.OnlyNull.OnlyNulls);
        Assert.True(DateSelection.Empty.IsEmpty);
        Assert.Equal(absolute, DateSelection.Between(from, to));
        Assert.NotEqual(absolute, absolute with { IncludeNull = true });
        Assert.Throws<ArgumentException>(() => DateSelection.Between(to, from));
        Assert.Throws<ArgumentException>(() => DateInterval.Between(to, from));
        Assert.Throws<ArgumentOutOfRangeException>(() => DateSelection.Relative((DatePreset)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => DateInterval.Relative((DatePreset)99));
        Assert.Null(DateInterval.Between(null, null).From);
    }

    /// <summary>Concept §5: a date selection is a set of parts, intervals and presets mixed, combined as OR.</summary>
    [Fact]
    public void DateSelection_is_a_set_of_parts()
    {
        var january = DateInterval.Between(TestData.Instant("2026-01-01T00:00:00Z"), TestData.Instant("2026-02-01T00:00:00Z"));
        var march = DateInterval.Between(TestData.Instant("2026-03-01T00:00:00Z"), TestData.Instant("2026-04-01T00:00:00Z"));
        var recent = DateInterval.Relative(DatePreset.Last7Days);

        var selection = DateSelection.Empty.Toggle(january).Toggle(recent).Toggle(march);

        Assert.Equal(3, selection.Intervals.Count);
        Assert.Equal(selection, new DateSelection([march, recent, january]));
        Assert.Equal(selection.GetHashCode(), new DateSelection([march, recent, january]).GetHashCode());
        Assert.Equal([january, march], selection.Toggle(recent).Intervals);
        Assert.Same(selection, selection.Add(march));
        Assert.True(DateSelection.Relative(DatePreset.Today).Toggle(recent).Toggle(DateInterval.Relative(DatePreset.Today)).Toggle(recent).IsEmpty);
        Assert.True(selection.ToggleNull().IncludeNull);
        Assert.True(DateSelection.OnlyNull.ToggleNull().IsEmpty);
        Assert.Equal(DateInterval.Relative(DatePreset.Today), DateInterval.Relative(DatePreset.Today));
        Assert.NotEqual(january, march);
    }

    [Fact]
    public void With_an_empty_range_or_date_selection_clears_the_facet()
    {
        var selections = Selections.Empty
            .With("Amount", RangeSelection.Between(0, 100))
            .With("OrderDate", DateSelection.Relative(DatePreset.Today));

        Assert.True(selections.With("Amount", RangeSelection.Empty).With("OrderDate", DateSelection.Empty).IsEmpty);
        Assert.Equal(2, selections.With("Amount", RangeSelection.OnlyNull).Count);
    }

    /// <summary>The click on a bar (concept §5): the interval is added or removed, and the facet clears when the last part goes.</summary>
    [Fact]
    public void ToggleInterval_adds_removes_and_clears_when_the_last_part_goes()
    {
        var low = new RangeInterval(null, 100, toInclusive: false);
        var high = RangeInterval.AtLeast(1000);
        var today = DateInterval.Relative(DatePreset.Today);
        var march = DateInterval.Between(TestData.Instant("2026-03-01T00:00:00Z"), TestData.Instant("2026-04-01T00:00:00Z"));

        var one = Selections.Empty.ToggleInterval("Amount", low).ToggleInterval("OrderDate", today);
        var two = one.ToggleInterval("Amount", high).ToggleInterval("OrderDate", march);
        var backToOne = two.ToggleInterval("Amount", low).ToggleInterval("OrderDate", today);
        var none = backToOne.ToggleInterval("Amount", high).ToggleInterval("OrderDate", march);

        Assert.Equal(new RangeSelection([low]), one["Amount"]);
        Assert.Equal(DateSelection.Relative(DatePreset.Today), one["OrderDate"]);
        Assert.Equal(new RangeSelection([low, high]), two["Amount"]);
        Assert.Equal(new DateSelection([today, march]), two["OrderDate"]);
        Assert.Equal(RangeSelection.AtLeast(1000), backToOne["Amount"]);
        Assert.Equal(new DateSelection([march]), backToOne["OrderDate"]);
        Assert.True(none.IsEmpty);

        // The null rows are a part of their own: removing the last interval keeps the facet when they are selected.
        var withNull = Selections.Empty.With("Amount", RangeSelection.Between(0, 1) with { IncludeNull = true });
        Assert.Equal(RangeSelection.OnlyNull, withNull.ToggleInterval("Amount", RangeInterval.Between(0, 1))["Amount"]);
    }

    [Fact]
    public void ToggleInterval_on_another_kind_of_selection_throws()
    {
        var selections = Selections.Empty
            .With("Country", ValueSelection.Of("SE"))
            .With("OrderDate", DateSelection.Relative(DatePreset.Today));

        Assert.Throws<InvalidOperationException>(() => selections.ToggleInterval("Country", RangeInterval.Between(0, 1)));
        Assert.Throws<InvalidOperationException>(() => selections.ToggleInterval("OrderDate", RangeInterval.Between(0, 1)));
        Assert.Throws<InvalidOperationException>(() => selections.ToggleInterval("Country", DateInterval.Relative(DatePreset.Today)));
    }

    [Fact]
    public void Empty_has_no_entries()
    {
        Assert.True(Selections.Empty.IsEmpty);
        Assert.Equal(0, Selections.Empty.Count);
        Assert.Null(Selections.Empty["Country"]);
        Assert.False(Selections.Empty.TryGet("Country", out _));
    }

    [Fact]
    public void With_replaces_and_Clear_removes()
    {
        var selections = Selections.Empty
            .With("Country", ValueSelection.Of("SE"))
            .With("Amount", RangeSelection.Between(0, 100))
            .With("Country", ValueSelection.Of("NO"));

        Assert.Equal(2, selections.Count);
        Assert.Equal(ValueSelection.Of("NO"), selections["Country"]);
        Assert.Equal(["Amount", "Country"], selections.Keys);

        var cleared = selections.Clear("Country");
        Assert.Equal(1, cleared.Count);
        Assert.Null(cleared["Country"]);
        Assert.Same(cleared, cleared.Clear("Country"));
        Assert.True(selections.ClearAll().IsEmpty);
    }

    [Fact]
    public void With_an_empty_value_selection_clears_the_facet()
    {
        var selections = Selections.Empty.With("Country", ValueSelection.Of("SE"));

        Assert.True(selections.With("Country", ValueSelection.Of()).IsEmpty);
    }

    [Fact]
    public void Toggle_adds_removes_and_clears_when_last_value_goes()
    {
        var one = Selections.Empty.Toggle("Country", "SE");
        var two = one.Toggle("Country", "NO");
        var backToOne = two.Toggle("Country", "SE");
        var none = backToOne.Toggle("Country", "NO");

        Assert.Equal(ValueSelection.Of("SE"), one["Country"]);
        Assert.Equal(ValueSelection.Of("SE", "NO"), two["Country"]);
        Assert.Equal(ValueSelection.Of("NO"), backToOne["Country"]);
        Assert.True(none.IsEmpty);
    }

    [Fact]
    public void Toggle_can_select_null()
    {
        var selections = Selections.Empty.Toggle("Country", null);

        Assert.Equal(ValueSelection.Of(null), selections["Country"]);
    }

    [Fact]
    public void Toggle_on_a_non_value_selection_throws()
    {
        var selections = Selections.Empty.With("Amount", RangeSelection.Between(0, 100));

        Assert.Throws<InvalidOperationException>(() => selections.Toggle("Amount", 5));
    }

    [Fact]
    public void Equality_is_by_content_regardless_of_insertion_order()
    {
        var a = Selections.Empty.With("A", ValueSelection.Of(1)).With("B", RangeSelection.Between(0, 1));
        var b = Selections.Empty.With("B", RangeSelection.Between(0, 1)).With("A", ValueSelection.Of(1));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a.Clear("B"));
        Assert.NotEqual(a, a.With("A", ValueSelection.Of(2)));
        Assert.Equal(Selections.Empty, Selections.Empty.With("A", ValueSelection.Of(1)).Clear("A"));
    }

    [Fact]
    public void Keys_are_validated()
    {
        Assert.Throws<ArgumentException>(() => Selections.Empty.With("", ValueSelection.Of(1)));
        Assert.Throws<ArgumentNullException>(() => Selections.Empty.With("A", null!));
        Assert.Throws<ArgumentException>(() => Selections.Empty.Toggle("", 1));
    }

    [Fact]
    public void Selections_equality_operators_compare_by_value()
    {
        var a = Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")).With("Amount", RangeSelection.AtLeast(100));
        var b = Selections.Empty.With("Amount", RangeSelection.AtLeast(100)).With("Country", ValueSelection.Of("NO", "SE"));
        Selections? none = null;

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a != a.Clear("Amount"));
        Assert.True(none == null);
        Assert.True(a != none);
        Assert.False(none == a);
    }
}
