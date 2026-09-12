namespace Linq2Dashboard.Indexing;

/// <summary>
/// Column for the metrics that read a property (design §3.3): one <see cref="double"/> per row,
/// NaN where null. Sum, average, min and max over a context set are produced in one pass by
/// <see cref="Aggregate"/> (design §4.6). Count needs no column. Immutable once built.
/// </summary>
internal sealed class MetricColumn
{
    private readonly double[] _values;
    private readonly MetricAggregate _total;

    private MetricColumn(double[] values, MetricAggregate total)
    {
        _values = values;
        _total = total;
    }

    public int RowCount => _values.Length;

    public ReadOnlySpan<double> Values => _values;

    /// <summary>Rows that have a value, over the whole dataset.</summary>
    public int ValueCount => _total.Count;

    public int NullCount => RowCount - ValueCount;

    /// <summary>Aggregate over the whole dataset, computed once at build.</summary>
    public MetricAggregate Total => _total;

    public static MetricColumn Build(int rowCount, RowReader<double> read)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentNullException.ThrowIfNull(read);

        var values = new double[rowCount];
        var accumulator = new Accumulator();
        for (int row = 0; row < rowCount; row++)
        {
            if (!read(row, out double value) || double.IsNaN(value))
            {
                values[row] = double.NaN;
                continue;
            }

            values[row] = value;
            accumulator.Add(value);
        }

        return new MetricColumn(values, accumulator.Result);
    }

    /// <summary>
    /// Sum, count of values, min and max over the rows in <paramref name="context"/>, skipping null
    /// (concept §4.4). When the context is the full dataset the precomputed total is returned.
    /// </summary>
    public MetricAggregate Aggregate(RowSet context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Length != RowCount)
        {
            throw new ArgumentException(
                $"Context has {context.Length} rows but the column has {RowCount}.", nameof(context));
        }

        if (context.IsFull)
        {
            return _total;
        }

        var accumulator = new Accumulator();
        ReadOnlySpan<double> values = _values;
        foreach (int row in context)
        {
            double value = values[row];
            if (!double.IsNaN(value))
            {
                accumulator.Add(value);
            }
        }

        return accumulator.Result;
    }

    /// <summary>Neumaier compensated summation alongside count, min and max.</summary>
    private struct Accumulator
    {
        private int _count;
        private double _sum;
        private double _compensation;
        private double _min;
        private double _max;

        public void Add(double value)
        {
            if (_count == 0)
            {
                _min = value;
                _max = value;
            }
            else
            {
                if (value < _min)
                {
                    _min = value;
                }

                if (value > _max)
                {
                    _max = value;
                }
            }

            _count++;
            double t = _sum + value;
            if (Math.Abs(_sum) >= Math.Abs(value))
            {
                _compensation += (_sum - t) + value;
            }
            else
            {
                _compensation += (value - t) + _sum;
            }

            _sum = t;
        }

        public MetricAggregate Result => _count == 0
            ? MetricAggregate.Empty
            : new MetricAggregate(_count, _sum + _compensation, _min, _max);
    }
}

/// <summary>
/// Result of aggregating a metric column over a set of rows. <see cref="Count"/> is the number of
/// rows that had a value. When it is zero every derived value is null, never zero (concept §4.4).
/// </summary>
internal readonly record struct MetricAggregate(int Count, double Sum, double Min, double Max)
{
    public static MetricAggregate Empty => new(0, double.NaN, double.NaN, double.NaN);

    public bool IsEmpty => Count == 0;

    public double? SumOrNull => IsEmpty ? null : Sum;

    public double? Average => IsEmpty ? null : Sum / Count;

    public double? MinOrNull => IsEmpty ? null : Min;

    public double? MaxOrNull => IsEmpty ? null : Max;
}
