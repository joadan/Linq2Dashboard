using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Tests.Indexing;

public class ValueColumnTests
{
    private static readonly string?[] Countries =
        ["SE", "NO", null, "SE", "DK", "no", null, "SE"];
    //    0     1     2     3     4     5     6     7

    private static ValueColumn<string> BuildCountries(IEqualityComparer<string>? comparer = null) =>
        Build(Countries, comparer);

    private static ValueColumn<TValue> Build<TValue>(TValue?[] values, IEqualityComparer<TValue>? comparer = null)
        where TValue : class =>
        ValueColumn<TValue>.Build(values.Length, (int row, out TValue value) =>
        {
            value = values[row]!;
            return value is not null;
        }, comparer);

    private static ValueColumn<TValue> BuildStructs<TValue>(TValue?[] values)
        where TValue : struct =>
        ValueColumn<TValue>.Build(values.Length, (int row, out TValue value) =>
        {
            value = values[row].GetValueOrDefault();
            return values[row].HasValue;
        });

    [Fact]
    public void Codes_are_assigned_in_first_seen_order_with_zero_for_null()
    {
        var column = BuildCountries();

        Assert.Equal(8, column.RowCount);
        Assert.Equal(3, column.DistinctCount);
        Assert.Equal([1, 2, 0, 1, 3, 2, 0, 1], column.Codes.ToArray());
        Assert.Equal("SE", column.ValueOf(1));
        Assert.Equal("NO", column.ValueOf(2));
        Assert.Equal("DK", column.ValueOf(3));
    }

    [Fact]
    public void Total_counts_are_computed_at_build()
    {
        var column = BuildCountries();

        Assert.Equal([2, 3, 2, 1], column.TotalCounts.ToArray());
        Assert.Equal(2, column.NullCount);
        Assert.True(column.HasNulls);
        Assert.Equal(3, column.TotalCount(1));
        Assert.Equal(column.RowCount, column.TotalCounts.ToArray().Sum());
    }

    [Fact]
    public void Strings_are_case_insensitive_by_default_and_keep_first_spelling()
    {
        var column = BuildCountries();

        Assert.True(column.TryGetCode("no", out int lower));
        Assert.True(column.TryGetCode("NO", out int upper));
        Assert.Equal(lower, upper);
        Assert.Equal("NO", column.ValueOf(lower));
        Assert.Equal(2, column.TotalCount(lower));
    }

    [Fact]
    public void A_supplied_comparer_overrides_the_default()
    {
        var column = BuildCountries(StringComparer.Ordinal);

        Assert.Equal(4, column.DistinctCount);
        Assert.True(column.TryGetCode("no", out int lower));
        Assert.True(column.TryGetCode("NO", out int upper));
        Assert.NotEqual(lower, upper);
        Assert.Equal(1, column.TotalCount(lower));
    }

    [Fact]
    public void Non_string_values_use_default_equality()
    {
        var column = BuildStructs<int>([5, null, 7, 5, 7, 7]);

        Assert.Equal(2, column.DistinctCount);
        Assert.Equal([1, 0, 2, 1, 2, 2], column.Codes.ToArray());
        Assert.Equal(1, column.NullCount);
        Assert.Equal(5, column.ValueOf(1));
        Assert.Equal(7, column.ValueOf(2));
        Assert.Same(EqualityComparer<int>.Default, ValueColumn.DefaultComparer<int>());
    }

    [Fact]
    public void TryGetCode_is_false_for_values_not_in_the_dataset()
    {
        var column = BuildCountries();

        Assert.False(column.TryGetCode("FI", out int code));
        Assert.Equal(0, code);
    }

    [Fact]
    public void ValueOf_and_TotalCount_reject_codes_out_of_range()
    {
        var column = BuildCountries();

        Assert.Throws<ArgumentOutOfRangeException>(() => column.ValueOf(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.ValueOf(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.TotalCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.TotalCount(4));
    }

    [Fact]
    public void Empty_dataset_builds_an_empty_column()
    {
        var column = Build<string>([]);

        Assert.Equal(0, column.RowCount);
        Assert.Equal(0, column.DistinctCount);
        Assert.Equal(0, column.NullCount);
        Assert.Equal([0], column.TotalCounts.ToArray());
        Assert.True(column.RowsWithCodes([0]).IsEmpty);
    }

    [Fact]
    public void RowsWithCodes_returns_rows_having_any_selected_code()
    {
        var column = BuildCountries();

        Assert.Equal([1, 4, 5], column.RowsWithCodes([2, 3]).Rows());
    }

    [Fact]
    public void RowsWithCodes_with_code_zero_selects_null_rows()
    {
        var column = BuildCountries();

        Assert.Equal([2, 6], column.RowsWithCodes([0]).Rows());
        Assert.Equal([0, 2, 3, 6, 7], column.RowsWithCodes([0, 1]).Rows());
    }

    [Fact]
    public void RowsWithCodes_with_no_codes_is_empty_and_with_all_codes_is_full()
    {
        var column = BuildCountries();

        Assert.True(column.RowsWithCodes([]).IsEmpty);
        Assert.True(column.RowsWithCodes([0, 1, 2, 3]).IsFull);
    }

    [Fact]
    public void RowsWithCodes_rejects_unknown_codes()
    {
        var column = BuildCountries();

        Assert.Throws<ArgumentOutOfRangeException>(() => column.RowsWithCodes([4]));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.RowsWithCodes([-1]));
    }

    [Fact]
    public void CountInto_counts_codes_over_the_context()
    {
        var column = BuildCountries();
        var context = RowSet.FromRows(8, [0, 1, 2, 5]); // SE, NO, null, no
        var counts = new int[4];

        column.CountInto(context, counts);

        Assert.Equal([1, 1, 2, 0], counts);
    }

    [Fact]
    public void CountInto_over_full_context_equals_total_counts()
    {
        var column = BuildCountries();
        var counts = new int[4];

        column.CountInto(RowSet.Full(8), counts);

        Assert.Equal(column.TotalCounts.ToArray(), counts);
    }

    [Fact]
    public void CountInto_clears_stale_values_first()
    {
        var column = BuildCountries();
        var counts = new int[] { 9, 9, 9, 9 };

        column.CountInto(RowSet.Empty(8), counts);

        Assert.Equal([0, 0, 0, 0], counts);
    }

    [Fact]
    public void CountInto_rejects_mismatched_lengths()
    {
        var column = BuildCountries();

        Assert.Throws<ArgumentException>(() => column.CountInto(RowSet.Full(7), new int[4]));
        Assert.Throws<ArgumentException>(() => column.CountInto(RowSet.Full(8), new int[3]));
    }

    [Fact]
    public void Counting_matches_brute_force_on_a_larger_dataset()
    {
        var random = new Random(42);
        var values = Enumerable.Range(0, 5000)
            .Select(_ => random.Next(6) == 0 ? null : (int?)random.Next(50))
            .ToArray();
        var column = BuildStructs(values);
        var contextRows = Enumerable.Range(0, 5000).Where(_ => random.Next(2) == 0).ToArray();
        var context = RowSet.FromRows(5000, contextRows);

        var counts = new int[column.DistinctCount + 1];
        column.CountInto(context, counts);

        var expected = new int[column.DistinctCount + 1];
        foreach (int row in contextRows)
        {
            int code = values[row] is int v ? (column.TryGetCode(v, out int c) ? c : -1) : 0;
            expected[code]++;
        }

        Assert.Equal(expected, counts);
        Assert.Equal(context.Count, counts.Sum());
    }

    [Fact]
    public void Build_rejects_invalid_arguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ValueColumn<string>.Build(-1, (int _, out string v) => { v = ""; return true; }));
        Assert.Throws<ArgumentNullException>(() => ValueColumn<string>.Build(1, null!));
    }
}
