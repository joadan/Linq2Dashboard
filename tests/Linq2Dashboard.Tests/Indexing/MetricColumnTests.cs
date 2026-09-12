using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Tests.Indexing;

public class MetricColumnTests
{
    private static readonly double?[] Amounts = [100, null, 250.5, -40, null, 1000, 0];
    //                                            0    1     2      3    4     5     6

    private static MetricColumn Build(double?[] values) =>
        MetricColumn.Build(values.Length, (int row, out double value) =>
        {
            value = values[row].GetValueOrDefault();
            return values[row].HasValue;
        });

    [Fact]
    public void Values_are_stored_with_NaN_for_null()
    {
        var column = Build(Amounts);

        Assert.Equal(7, column.RowCount);
        Assert.Equal(100, column.Values[0]);
        Assert.True(double.IsNaN(column.Values[1]));
        Assert.Equal(5, column.ValueCount);
        Assert.Equal(2, column.NullCount);
    }

    [Fact]
    public void A_NaN_read_as_a_value_is_treated_as_null()
    {
        var column = Build([1, double.NaN, 3]);

        Assert.Equal(2, column.ValueCount);
        Assert.Equal(4, column.Total.Sum);
    }

    [Fact]
    public void Total_is_computed_at_build_and_skips_null()
    {
        var column = Build(Amounts);

        Assert.Equal(5, column.Total.Count);
        Assert.Equal(1310.5, column.Total.Sum);
        Assert.Equal(-40, column.Total.Min);
        Assert.Equal(1000, column.Total.Max);
        Assert.Equal(262.1, column.Total.Average!.Value, precision: 10);
    }

    [Fact]
    public void Aggregate_over_a_context_skips_null_and_divides_by_values()
    {
        var column = Build(Amounts);

        var result = column.Aggregate(RowSet.FromRows(7, [0, 1, 3, 4])); // 100, null, -40, null

        Assert.Equal(2, result.Count);
        Assert.Equal(60, result.Sum);
        Assert.Equal(-40, result.Min);
        Assert.Equal(100, result.Max);
        Assert.Equal(30, result.Average);
    }

    [Fact]
    public void Aggregate_over_full_context_returns_the_total()
    {
        var column = Build(Amounts);

        Assert.Equal(column.Total, column.Aggregate(RowSet.Full(7)));
    }

    [Fact]
    public void Aggregate_with_no_values_reports_null_not_zero()
    {
        var column = Build(Amounts);

        var onlyNulls = column.Aggregate(RowSet.FromRows(7, [1, 4]));
        var empty = column.Aggregate(RowSet.Empty(7));

        foreach (var result in new[] { onlyNulls, empty })
        {
            Assert.True(result.IsEmpty);
            Assert.Equal(0, result.Count);
            Assert.Null(result.SumOrNull);
            Assert.Null(result.Average);
            Assert.Null(result.MinOrNull);
            Assert.Null(result.MaxOrNull);
        }
    }

    [Fact]
    public void Single_value_is_its_own_min_max_sum_and_average()
    {
        var column = Build(Amounts);

        var result = column.Aggregate(RowSet.FromRows(7, [2]));

        Assert.Equal(new MetricAggregate(1, 250.5, 250.5, 250.5), result);
        Assert.Equal(250.5, result.Average);
    }

    [Fact]
    public void Zero_is_a_value_not_null()
    {
        var column = Build(Amounts);

        var result = column.Aggregate(RowSet.FromRows(7, [6]));

        Assert.Equal(1, result.Count);
        Assert.Equal(0, result.SumOrNull);
        Assert.Equal(0, result.Average);
    }

    [Fact]
    public void Aggregate_rejects_a_context_of_different_length()
    {
        var column = Build(Amounts);

        Assert.Throws<ArgumentException>(() => column.Aggregate(RowSet.Full(6)));
    }

    [Fact]
    public void Empty_dataset_builds_with_an_empty_total()
    {
        var column = Build([]);

        Assert.Equal(0, column.RowCount);
        Assert.True(column.Total.IsEmpty);
        Assert.True(column.Aggregate(RowSet.Empty(0)).IsEmpty);
    }

    [Fact]
    public void Summation_is_compensated()
    {
        // Naive left-to-right double summation of these gives 0 or a rounding error; the true sum is 2.
        var column = Build([1.0, 1e100, 1.0, -1e100]);

        Assert.Equal(2.0, column.Total.Sum);
    }

    [Fact]
    public void Aggregate_matches_brute_force_on_a_larger_dataset()
    {
        var random = new Random(3);
        var values = Enumerable.Range(0, 5000)
            .Select(_ => random.Next(10) == 0 ? null : (double?)Math.Round(random.NextDouble() * 2000 - 500, 2))
            .ToArray();
        var column = Build(values);
        var contextRows = Enumerable.Range(0, 5000).Where(_ => random.Next(2) == 0).ToArray();

        var result = column.Aggregate(RowSet.FromRows(5000, contextRows));

        var present = contextRows.Where(r => values[r].HasValue).Select(r => values[r]!.Value).ToArray();
        Assert.Equal(present.Length, result.Count);
        Assert.Equal(present.Sum(), result.Sum, precision: 6);
        Assert.Equal(present.Min(), result.Min);
        Assert.Equal(present.Max(), result.Max);
        Assert.Equal(present.Average(), result.Average!.Value, precision: 6);
    }
}
