namespace Linq2Dashboard.Indexing;

/// <summary>
/// Column for a numeric range facet (design §3.3). Values are stored as <see cref="double"/>, NaN
/// where null. Each row also carries a bucket code fixed at build: 0 for null, 1..B for the bucket
/// (concept §5, "range buckets are fixed at initialisation"). Immutable once built.
/// </summary>
internal sealed class RangeColumn
{
    private readonly double[] values;
    private readonly int[] bucketCodes;
    private readonly double[] edges;
    private readonly int[] totalCounts;

    private RangeColumn(double[] values, RowSet nulls, int[] bucketCodes, double[] edges, int[] totalCounts, double min, double max)
    {
        this.values = values;
        Nulls = nulls;
        this.bucketCodes = bucketCodes;
        this.edges = edges;
        this.totalCounts = totalCounts;
        Min = min;
        Max = max;
    }

    public int RowCount => values.Length;

    /// <summary>Rows whose value is null.</summary>
    public RowSet Nulls { get; }

    public int NullCount => Nulls.Count;

    public bool HasNulls => NullCount > 0;

    /// <summary>Smallest non-null value in the dataset; NaN when there is none.</summary>
    public double Min { get; }

    /// <summary>Largest non-null value in the dataset; NaN when there is none.</summary>
    public double Max { get; }

    /// <summary>Number of buckets, B. Valid bucket codes are 0..B.</summary>
    public int BucketCount => edges.Length == 0 ? 0 : edges.Length - 1;

    public ReadOnlySpan<double> Values => values;

    public ReadOnlySpan<int> BucketCodes => bucketCodes;

    /// <summary>Total count per bucket code, index 0 being null.</summary>
    public ReadOnlySpan<int> TotalCounts => totalCounts;

    /// <summary>
    /// Bounds of bucket <paramref name="index"/> (0-based). The bucket covers <c>[From, To)</c>,
    /// except that the last bucket also includes <c>To</c>. Bounds may be infinite.
    /// </summary>
    public (double From, double To) Bucket(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, BucketCount);
        return (edges[index], edges[index + 1]);
    }

    public static RangeColumn Build(int rowCount, RowReader<double> read, RangeBucketing bucketing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(bucketing);

        var values = new double[rowCount];
        var nulls = new RowSetBuilder(rowCount);
        double min = double.NaN;
        double max = double.NaN;

        for (int row = 0; row < rowCount; row++)
        {
            if (!read(row, out double value) || double.IsNaN(value))
            {
                values[row] = double.NaN;
                nulls.Set(row);
                continue;
            }

            values[row] = value;
            if (double.IsNaN(min) || value < min)
            {
                min = value;
            }

            if (double.IsNaN(max) || value > max)
            {
                max = value;
            }
        }

        double[] edges = bucketing.Edges(min, max);
        var codes = new int[rowCount];
        for (int row = 0; row < rowCount; row++)
        {
            double value = values[row];
            codes[row] = double.IsNaN(value) ? 0 : BucketIndex(edges, value) + 1;
        }

        int bucketCount = edges.Length == 0 ? 0 : edges.Length - 1;
        int[] totals = CodeColumn.Totals(codes, bucketCount + 1);
        return new RangeColumn(values, nulls.Build(), codes, edges, totals, min, max);
    }

    /// <summary>
    /// Rows whose value lies in the interval (design §4.1). A null bound is unbounded on that side.
    /// Null rows are excluded unless <paramref name="includeNull"/> (concept §4.8).
    /// </summary>
    public RowSet RowsInInterval(double? from, double? to, bool fromInclusive = true, bool toInclusive = true, bool includeNull = false)
    {
        if (from is double f && double.IsNaN(f))
        {
            throw new ArgumentException("Bound must not be NaN.", nameof(from));
        }

        if (to is double t && double.IsNaN(t))
        {
            throw new ArgumentException("Bound must not be NaN.", nameof(to));
        }

        if (from is double lo && to is double hi && lo > hi)
        {
            throw new ArgumentException($"Interval start {lo} is after its end {hi}.", nameof(from));
        }

        double low = from ?? double.NegativeInfinity;
        double high = to ?? double.PositiveInfinity;
        bool lowInclusive = from is null || fromInclusive;
        bool highInclusive = to is null || toInclusive;

        var builder = new RowSetBuilder(RowCount);
        ReadOnlySpan<double> all = values;
        for (int row = 0; row < all.Length; row++)
        {
            double value = all[row];
            bool aboveLow = lowInclusive ? value >= low : value > low;
            bool belowHigh = highInclusive ? value <= high : value < high;
            if (aboveLow && belowHigh)
            {
                builder.Set(row);
            }
        }

        RowSet result = builder.Build();
        return includeNull ? result.Or(Nulls) : result;
    }

    /// <summary>Filtered counts per bucket code over the context (design §4.5). Length B + 1; index 0 is null.</summary>
    public void CountInto(RowSet context, Span<int> counts) =>
        CodeColumn.CountInto(bucketCodes, totalCounts, context, counts);

    /// <summary>Index of the bucket containing <paramref name="value"/>. Edges must be non-empty and cover the value.</summary>
    internal static int BucketIndex(ReadOnlySpan<double> edges, double value)
    {
        int last = edges.Length - 2;
        if (value >= edges[^1])
        {
            return last;
        }

        int index = edges.BinarySearch(value);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return Math.Clamp(index, 0, last);
    }
}
