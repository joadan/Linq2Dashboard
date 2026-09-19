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
        Assert.Same(two, two.Remove(RangeInterval.Between(1, 2)));
        Assert.NotEqual(two, two.ToggleNull());
        Assert.True(two.ToggleNull().IncludeNull);
        Assert.Equal(two, two.ToggleNull().ToggleNull());
        Assert.True(RangeSelection.Empty.ToggleNull().OnlyNulls);
        Assert.True(RangeSelection.OnlyNull.ToggleNull().IsEmpty);
        Assert.Throws<ArgumentException>(() => new RangeSelection([low, null!]));
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
