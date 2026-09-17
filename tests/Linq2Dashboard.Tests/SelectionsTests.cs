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

        var closed = RangeSelection.Between(100, 500);
        Assert.Equal((100, 500, true, true, false), (closed.From, closed.To, closed.FromInclusive, closed.ToInclusive, closed.IncludeNull));
        Assert.Null(RangeSelection.AtLeast(5).To);
        Assert.Null(RangeSelection.AtMost(5).From);
        Assert.True(RangeSelection.OnlyNull.OnlyNulls);
        Assert.Equal(closed, closed with { });
        Assert.NotEqual(closed, closed with { ToInclusive = false });
    }

    [Fact]
    public void DateSelection_is_absolute_or_relative()
    {
        var from = TestData.Instant("2026-03-01T00:00:00Z");
        var to = TestData.Instant("2026-04-01T00:00:00Z");

        var absolute = DateSelection.Between(from, to);
        var relative = DateSelection.Relative(DatePreset.Last7Days);

        Assert.False(absolute.IsRelative);
        Assert.Equal((from, to), (absolute.From, absolute.To));
        Assert.True(relative.IsRelative);
        Assert.Equal(DatePreset.Last7Days, relative.Preset);
        Assert.Null(relative.From);
        Assert.True(DateSelection.OnlyNull.OnlyNulls);
        Assert.Equal(absolute, DateSelection.Between(from, to));
        Assert.NotEqual(absolute, absolute with { IncludeNull = true });
        Assert.Throws<ArgumentException>(() => DateSelection.Between(to, from));
        Assert.Throws<ArgumentOutOfRangeException>(() => DateSelection.Relative((DatePreset)99));
        Assert.Null(DateSelection.Between(null, null).From);
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
