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
    public void Auto_buckets_have_round_edges_and_about_the_requested_count()
    {
        // 3 to 97 over 5 buckets: the raw width 18.8 rounds to the step 20, aligned to multiples of 20.
        var column = Build([3, 25, 50, 75, 97, null], RangeBucketing.Auto(5));

        Assert.Equal(5, column.BucketCount);
        Assert.Equal((0, 20), column.Bucket(0));
        Assert.Equal((20, 40), column.Bucket(1));
        Assert.Equal((40, 60), column.Bucket(2));
        Assert.Equal((60, 80), column.Bucket(3));
        Assert.Equal((80, 100), column.Bucket(4));
        Assert.Equal([1, 2, 3, 4, 5, 0], column.BucketCodes.ToArray());
    }

    [Fact]
    public void Auto_buckets_have_no_open_tail_when_nothing_lies_beyond_the_round_edges()
    {
        var column = Build([0, 25, 50, 75, 100], RangeBucketing.Auto(4));

        Assert.Equal(0, column.Bucket(0).From);
        Assert.Equal(100, column.Bucket(column.BucketCount - 1).To);
        Assert.Equal(column.BucketCount, column.BucketCodes[4]); // 100 sits in the closed last bucket
    }

    [Fact]
    public void Auto_buckets_put_outliers_in_open_tails_and_size_the_step_by_the_body()
    {
        // 980 values spread over 0 to 97.9, ten far below and ten far above.
        var values = new double?[1000];
        for (int i = 0; i < 980; i++)
        {
            values[i] = i / 10.0;
        }

        for (int i = 980; i < 990; i++)
        {
            values[i] = -1000;
        }

        for (int i = 990; i < 1000; i++)
        {
            values[i] = 10000;
        }

        var column = Build(values, RangeBucketing.Auto(10));

        Assert.Equal(12, column.BucketCount);
        Assert.Equal((double.NegativeInfinity, 0), column.Bucket(0));
        Assert.Equal((0, 10), column.Bucket(1));
        Assert.Equal((90, 100), column.Bucket(10));
        Assert.Equal((100, double.PositiveInfinity), column.Bucket(11));
        Assert.Equal(10, column.TotalCounts[1]);
        Assert.Equal(10, column.TotalCounts[12]);
        Assert.Equal(980, column.TotalCounts[2..12].ToArray().Sum());
    }

    [Fact]
    public void Auto_bucket_edges_with_a_fractional_step_are_exact_decimals()
    {
        var column = Build([0.1, 0.3, 0.5, 0.7, 0.9], RangeBucketing.Auto(4));

        Assert.Equal(5, column.BucketCount);
        Assert.Equal([0, 0.2, 0.4, 0.6, 0.8], Enumerable.Range(0, 5).Select(i => column.Bucket(i).From));
        Assert.Equal(1.0, column.Bucket(4).To);
    }

    [Fact]
    public void Auto_buckets_cover_negative_values()
    {
        var column = Build([-50, -10, 0, 10, 50], RangeBucketing.Auto(4));

        Assert.Equal(-60, column.Bucket(0).From);
        Assert.Equal(60, column.Bucket(column.BucketCount - 1).To);
        Assert.All(Enumerable.Range(0, column.BucketCount), i => Assert.Equal(20, column.Bucket(i).To - column.Bucket(i).From));
    }

    [Fact]
    public void Auto_buckets_fall_back_to_the_whole_range_when_the_body_has_one_value()
    {
        // 99 zeros and a 3: the 2nd and 98th percentile are both 0, so the step comes from min to max.
        var values = Enumerable.Repeat<double?>(0, 99).Concat([3]).ToArray();

        var column = Build(values, RangeBucketing.Auto(3));

        Assert.Equal(3, column.BucketCount);
        Assert.Equal((0, 1), column.Bucket(0));
        Assert.Equal((2, 3), column.Bucket(2));
    }

    [Fact]
    public void Auto_buckets_over_a_large_column_read_a_sample_and_are_deterministic()
    {
        var values = new double?[250_000];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = i % 7 == 0 ? null : i % 1000;
        }

        var first = Build(values, RangeBucketing.Auto(10));
        var second = Build(values, RangeBucketing.Auto(10));

        Assert.Equal(10, first.BucketCount);
        Assert.Equal((0, 100), first.Bucket(0));
        Assert.Equal((900, 1000), first.Bucket(9));
        Assert.Equal(
            Enumerable.Range(0, first.BucketCount).Select(first.Bucket),
            Enumerable.Range(0, second.BucketCount).Select(second.Bucket));
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
