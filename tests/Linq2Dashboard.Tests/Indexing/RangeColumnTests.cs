using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Tests.Indexing;

public class RangeColumnTests
{
    private static readonly double?[] Amounts =
        [50, 100, null, 250, 500, 999.5, 1000, null, 2500, 0];
    //    0    1     2    3    4    5      6     7     8    9

    private static RangeColumn Build(double?[] values, RangeBucketing bucketing) =>
        RangeColumn.Build(values.Length, (int row, out double value) =>
        {
            value = values[row].GetValueOrDefault();
            return values[row].HasValue;
        }, bucketing);

    private static RangeColumn BuildAmounts(RangeBucketing? bucketing = null) =>
        Build(Amounts, bucketing ?? RangeBucketing.Explicit(100, 500, 1000));

    [Fact]
    public void Values_are_stored_with_NaN_for_null_and_bounds_from_the_dataset()
    {
        var column = BuildAmounts();

        Assert.Equal(10, column.RowCount);
        Assert.True(double.IsNaN(column.Values[2]));
        Assert.True(double.IsNaN(column.Values[7]));
        Assert.Equal(250, column.Values[3]);
        Assert.Equal([2, 7], column.Nulls.Rows());
        Assert.Equal(2, column.NullCount);
        Assert.Equal(0, column.Min);
        Assert.Equal(2500, column.Max);
    }

    [Fact]
    public void A_NaN_read_as_a_value_is_treated_as_null()
    {
        var column = Build([1, double.NaN, 3], RangeBucketing.Auto(2));

        Assert.Equal([1], column.Nulls.Rows());
        Assert.Equal(1, column.Min);
        Assert.Equal(3, column.Max);
    }

    [Fact]
    public void Explicit_cuts_give_open_ended_first_and_last_buckets()
    {
        var column = BuildAmounts();

        Assert.Equal(4, column.BucketCount);
        Assert.Equal((double.NegativeInfinity, 100), column.Bucket(0));
        Assert.Equal((100, 500), column.Bucket(1));
        Assert.Equal((500, 1000), column.Bucket(2));
        Assert.Equal((1000, double.PositiveInfinity), column.Bucket(3));
    }

    [Fact]
    public void Explicit_cuts_assign_values_on_a_cut_to_the_upper_bucket()
    {
        var column = BuildAmounts();

        //                     50 100 null 250 500 999.5 1000 null 2500 0
        Assert.Equal([1, 2, 0, 2, 3, 3, 4, 0, 4, 1], column.BucketCodes.ToArray());
        Assert.Equal([2, 2, 2, 2, 2], column.TotalCounts.ToArray());
    }

    [Fact]
    public void Explicit_with_no_cuts_is_a_single_bucket_over_everything()
    {
        var column = BuildAmounts(RangeBucketing.Explicit());

        Assert.Equal(1, column.BucketCount);
        Assert.Equal((double.NegativeInfinity, double.PositiveInfinity), column.Bucket(0));
        Assert.Equal([2, 8], column.TotalCounts.ToArray());
    }

    [Fact]
    public void Auto_buckets_are_equal_width_over_the_dataset_bounds()
    {
        var column = Build([0, 25, 50, 75, 100, null], RangeBucketing.Auto(4));

        Assert.Equal(4, column.BucketCount);
        Assert.Equal((0, 25), column.Bucket(0));
        Assert.Equal((25, 50), column.Bucket(1));
        Assert.Equal((50, 75), column.Bucket(2));
        Assert.Equal((75, 100), column.Bucket(3));
        // 0→1, 25→2, 50→3, 75→4, 100→4 (last bucket includes its upper edge), null→0
        Assert.Equal([1, 2, 3, 4, 4, 0], column.BucketCodes.ToArray());
    }

    [Fact]
    public void Auto_buckets_with_no_values_give_no_buckets()
    {
        var column = Build([null, null], RangeBucketing.Auto(5));

        Assert.Equal(0, column.BucketCount);
        Assert.True(double.IsNaN(column.Min));
        Assert.Equal([0, 0], column.BucketCodes.ToArray());
        Assert.Equal([2], column.TotalCounts.ToArray());
    }

    [Fact]
    public void Auto_buckets_with_a_single_distinct_value_give_one_bucket()
    {
        var column = Build([7, 7, null, 7], RangeBucketing.Auto(5));

        Assert.Equal(1, column.BucketCount);
        Assert.Equal((7, 7), column.Bucket(0));
        Assert.Equal([1, 1, 0, 1], column.BucketCodes.ToArray());
    }

    [Fact]
    public void Empty_dataset_builds()
    {
        var column = Build([], RangeBucketing.Explicit(10));

        Assert.Equal(0, column.RowCount);
        Assert.Equal(2, column.BucketCount);
        Assert.Equal([0, 0, 0], column.TotalCounts.ToArray());
        Assert.True(column.RowsInInterval(null, null).IsEmpty);
    }

    [Fact]
    public void Invalid_bucketing_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => RangeBucketing.Explicit(100, 100));
        Assert.Throws<ArgumentException>(() => RangeBucketing.Explicit(500, 100));
        Assert.Throws<ArgumentException>(() => RangeBucketing.Explicit(double.NaN));
        Assert.Throws<ArgumentException>(() => RangeBucketing.Explicit(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => RangeBucketing.Auto(0));
    }

    [Fact]
    public void Bucket_rejects_index_out_of_range()
    {
        var column = BuildAmounts();

        Assert.Throws<ArgumentOutOfRangeException>(() => column.Bucket(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.Bucket(4));
    }

    [Fact]
    public void Closed_interval_includes_both_ends()
    {
        var column = BuildAmounts();

        Assert.Equal([1, 3, 4], column.RowsInInterval(100, 500).Rows());
    }

    [Fact]
    public void Half_open_interval_excludes_the_upper_end()
    {
        var column = BuildAmounts();

        Assert.Equal([1, 3], column.RowsInInterval(100, 500, toInclusive: false).Rows());
    }

    [Fact]
    public void Open_interval_excludes_both_ends()
    {
        var column = BuildAmounts();

        Assert.Equal([3], column.RowsInInterval(100, 500, fromInclusive: false, toInclusive: false).Rows());
    }

    [Fact]
    public void Unbounded_sides_take_everything_on_that_side_but_never_null()
    {
        var column = BuildAmounts();

        Assert.Equal([4, 5, 6, 8], column.RowsInInterval(500, null).Rows());
        Assert.Equal([0, 1, 9], column.RowsInInterval(null, 100).Rows());
        Assert.Equal([0, 1, 3, 4, 5, 6, 8, 9], column.RowsInInterval(null, null).Rows());
    }

    [Fact]
    public void Degenerate_interval_selects_equal_values()
    {
        var column = BuildAmounts();

        Assert.Equal([6], column.RowsInInterval(1000, 1000).Rows());
        Assert.True(column.RowsInInterval(1000, 1000, toInclusive: false).IsEmpty);
    }

    [Fact]
    public void IncludeNull_adds_the_null_rows()
    {
        var column = BuildAmounts();

        Assert.Equal([1, 2, 3, 4, 7], column.RowsInInterval(100, 500, includeNull: true).Rows());
        Assert.Equal([2, 7], column.RowsInInterval(3000, null, includeNull: true).Rows());
    }

    [Fact]
    public void Bucket_bounds_used_as_a_half_open_selection_reproduce_the_bucket()
    {
        var column = BuildAmounts();

        for (int i = 0; i < column.BucketCount; i++)
        {
            (double from, double to) = column.Bucket(i);
            bool last = i == column.BucketCount - 1;
            var rows = column.RowsInInterval(
                double.IsInfinity(from) ? null : from,
                double.IsInfinity(to) ? null : to,
                toInclusive: last);

            var expected = Enumerable.Range(0, column.RowCount).Where(r => column.BucketCodes[r] == i + 1);
            Assert.Equal(expected, rows.Rows());
        }
    }

    [Fact]
    public void Invalid_intervals_are_rejected()
    {
        var column = BuildAmounts();

        Assert.Throws<ArgumentException>(() => column.RowsInInterval(500, 100));
        Assert.Throws<ArgumentException>(() => column.RowsInInterval(double.NaN, 100));
        Assert.Throws<ArgumentException>(() => column.RowsInInterval(0, double.NaN));
    }

    [Fact]
    public void CountInto_counts_buckets_over_the_context()
    {
        var column = BuildAmounts();
        var counts = new int[5];

        column.CountInto(RowSet.FromRows(10, [0, 2, 4, 8]), counts); // 50, null, 500, 2500

        Assert.Equal([1, 1, 0, 1, 1], counts);
    }

    [Fact]
    public void CountInto_over_full_context_equals_totals()
    {
        var column = BuildAmounts();
        var counts = new int[5];

        column.CountInto(RowSet.Full(10), counts);

        Assert.Equal(column.TotalCounts.ToArray(), counts);
    }

    [Fact]
    public void Counting_matches_brute_force_on_a_larger_dataset()
    {
        var random = new Random(7);
        var values = Enumerable.Range(0, 5000)
            .Select(_ => random.Next(10) == 0 ? null : (double?)Math.Round(random.NextDouble() * 1000, 2))
            .ToArray();
        var column = Build(values, RangeBucketing.Auto(8));
        var contextRows = Enumerable.Range(0, 5000).Where(_ => random.Next(2) == 0).ToArray();

        var counts = new int[column.BucketCount + 1];
        column.CountInto(RowSet.FromRows(5000, contextRows), counts);

        var expected = new int[column.BucketCount + 1];
        foreach (int row in contextRows)
        {
            expected[column.BucketCodes[row]]++;
        }

        Assert.Equal(expected, counts);
        Assert.Equal(contextRows.Length, counts.Sum());
    }
}
