using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Queries;

/// <summary>
/// A query dimension over existing columns (design §10.3): one code per row, 0 for null, or a set of
/// codes for a multi-valued facet, plus the cell and natural order of each code.
/// </summary>
internal abstract class GroupColumn(int codeCount, QueryColumnKind kind)
{
    /// <summary>Codes run from 0 (null) to <c>CodeCount - 1</c>.</summary>
    public int CodeCount { get; } = codeCount;

    public QueryColumnKind Kind { get; } = kind;

    public virtual DateGranularity? Granularity => null;

    /// <summary>True when a row may have several codes; <see cref="CodesAt"/> then replaces <see cref="CodeAt"/>.</summary>
    public virtual bool IsMultiValued => false;

    public abstract int CodeAt(int row);

    /// <summary>The codes of a row under a multi-valued facet. Empty means the null value.</summary>
    public virtual ReadOnlySpan<int> CodesAt(int row) => throw new NotSupportedException();

    /// <summary>The answer's cell for a non-null code (design §10.2).</summary>
    public abstract object Cell(int code);

    /// <summary>Natural order (concept §4.11): intervals and periods ascending, values by value, null last.</summary>
    public int Compare(int a, int b)
    {
        if (a == b)
        {
            return 0;
        }

        if (a == 0 || b == 0)
        {
            return a == 0 ? 1 : -1;
        }

        return CompareNonNull(a, b);
    }

    /// <summary>Codes of buckets and periods are already in order; value columns compare their values.</summary>
    protected virtual int CompareNonNull(int a, int b) => a.CompareTo(b);
}

internal sealed class ValueGroupColumn<TValue>(ValueColumn<TValue> column, string?[]? labels)
    : GroupColumn(column.DistinctCount + 1, QueryColumnKind.Value)
{
    public override int CodeAt(int row) => column.CodeAt(row);

    public override object Cell(int code) => ValueCells.Cell(column, labels, code);

    protected override int CompareNonNull(int a, int b) => ValueCells.Compare(column, a, b);
}

internal sealed class MultiValueGroupColumn<TValue>(MultiValueColumn<TValue> column, string?[]? labels)
    : GroupColumn(column.DistinctCount + 1, QueryColumnKind.Value)
{
    public override bool IsMultiValued => true;

    public override int CodeAt(int row) => throw new NotSupportedException();

    public override ReadOnlySpan<int> CodesAt(int row) => column.CodesOf(row);

    public override object Cell(int code) => ValueCells.Cell(column, labels, code);

    protected override int CompareNonNull(int a, int b) => ValueCells.Compare(column, a, b);
}

internal sealed class RangeGroupColumn(RangeColumn column) : GroupColumn(column.BucketCount + 1, QueryColumnKind.Interval)
{
    public override int CodeAt(int row) => column.BucketCodes[row];

    /// <summary>The bucket's <c>[From, To)</c>, the last bucket closed as the column holds it (design §3.3), open where it is unbounded.</summary>
    public override object Cell(int code)
    {
        (double from, double to) = column.Bucket(code - 1);
        return new RangeInterval(
            double.IsInfinity(from) ? null : from,
            double.IsInfinity(to) ? null : to,
            fromInclusive: true,
            toInclusive: column.IncludesTo(code - 1));
    }
}

/// <summary>
/// A date facet grouped by a coarser period that its own periods nest in (a month in its quarter and year, a day in
/// anything): each bucket maps to the coarser period once, so rows are read through the bucket codes (design §10.3).
/// </summary>
internal sealed class CoarserDateGroupColumn : GroupColumn
{
    private readonly DateColumn column;
    private readonly int[] codeOfBucket;
    private readonly DateTime[] starts;
    private readonly DateGranularity granularity;

    private CoarserDateGroupColumn(DateColumn column, int[] codeOfBucket, DateTime[] starts, DateGranularity granularity)
        : base(starts.Length + 1, QueryColumnKind.Period)
    {
        this.column = column;
        this.codeOfBucket = codeOfBucket;
        this.starts = starts;
        this.granularity = granularity;
    }

    public override DateGranularity? Granularity => granularity;

    /// <summary>Whether every period of <paramref name="own"/> lies inside one period of <paramref name="coarser"/>. Weeks nest in nothing but weeks.</summary>
    public static bool Nests(DateGranularity own, DateGranularity coarser) => (own, coarser) switch
    {
        (DateGranularity.Day, _) => true,
        (DateGranularity.Month, DateGranularity.Quarter or DateGranularity.Year) => true,
        (DateGranularity.Quarter, DateGranularity.Year) => true,
        _ => false,
    };

    public static CoarserDateGroupColumn Create(DateColumn column, DateGranularity granularity)
    {
        var codeOfBucket = new int[column.BucketCount + 1];
        var starts = new List<DateTime>();
        for (int bucket = 0; bucket < column.BucketCount; bucket++)
        {
            DateTime start = DateColumn.PeriodStart(column.BucketStart(bucket), granularity);
            if (starts.Count == 0 || starts[^1] != start)
            {
                starts.Add(start);
            }

            codeOfBucket[bucket + 1] = starts.Count;
        }

        return new CoarserDateGroupColumn(column, codeOfBucket, starts.ToArray(), granularity);
    }

    public override int CodeAt(int row) => codeOfBucket[column.BucketCodes[row]];

    public override object Cell(int code)
    {
        DateTime start = starts[code - 1];
        return new QueryPeriod(
            DateInterval.Between(column.ToInstant(start), column.ToInstant(DateColumn.NextPeriodStart(start, granularity))),
            start,
            granularity);
    }
}

internal sealed class DateGroupColumn(DateColumn column) : GroupColumn(column.BucketCount + 1, QueryColumnKind.Period)
{
    public override DateGranularity? Granularity => column.Granularity;

    public override int CodeAt(int row) => column.BucketCodes[row];

    public override object Cell(int code)
    {
        (DateTimeOffset from, DateTimeOffset to) = column.BucketInterval(code - 1);
        return new QueryPeriod(DateInterval.Between(from, to), column.BucketStart(code - 1), column.Granularity);
    }
}

/// <summary>Cells and order shared by the value and multi-valued group columns.</summary>
internal static class ValueCells
{
    public static object Cell<TValue>(IValueColumn<TValue> column, string?[]? labels, int code) =>
        new QueryValueCell(column.ValueOf(code), labels?[code - 1]);

    /// <summary>By value when the type has an order (strings ignoring case), otherwise in first-seen order.</summary>
    public static int Compare<TValue>(IValueColumn<TValue> column, int a, int b) =>
        Compare(column.ValueOf(a), column.ValueOf(b), a, b);

    /// <summary>Two values of codes <paramref name="a"/> and <paramref name="b"/> by value, then by code, so the order is total and deterministic.</summary>
    public static int Compare<TValue>(TValue x, TValue y, int a, int b)
    {
        int result = ValueOrder<TValue>.Comparer is IComparer<TValue> comparer ? comparer.Compare(x, y) : 0;
        return result != 0 ? result : a.CompareTo(b);
    }

    private static class ValueOrder<TValue>
    {
        public static readonly IComparer<TValue>? Comparer = Create();

        private static IComparer<TValue>? Create()
        {
            if (typeof(TValue) == typeof(string))
            {
                return (IComparer<TValue>)(object)StringComparer.OrdinalIgnoreCase;
            }

            Type type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
            bool comparable = typeof(IComparable).IsAssignableFrom(type)
                || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IComparable<>));
            return comparable ? Comparer<TValue>.Default : null;
        }
    }
}

/// <summary>One number per row for sum, average, min and max; NaN for null (design §10.3).</summary>
internal abstract class NumberSource
{
    /// <summary>The column the numbers come from, so two values over one column share one accumulation.</summary>
    public abstract object Identity { get; }

    public abstract double ValueAt(int row);
}

internal sealed class ColumnNumbers(object identity, double[] values) : NumberSource
{
    public override object Identity => identity;

    public override double ValueAt(int row) => values[row];
}

/// <summary>A numeric value facet: one double per code, read through the row's code.</summary>
internal sealed class CodedNumbers<TValue>(ValueColumn<TValue> column, double[] valueOfCode) : NumberSource
{
    public override object Identity => column;

    public override double ValueAt(int row) => valueOfCode[column.CodeAt(row)];
}

/// <summary>Keys whose distinct count per group a query reports (design §10.3). Null rows add nothing.</summary>
internal abstract class DistinctSource
{
    public abstract object Identity { get; }

    public abstract void AddTo(int row, int group, HashSet<(int Group, long Key)> seen);
}

internal sealed class CodeKeys(object identity, Func<int, int> codeAt) : DistinctSource
{
    public override object Identity => identity;

    public override void AddTo(int row, int group, HashSet<(int Group, long Key)> seen)
    {
        int code = codeAt(row);
        if (code != 0)
        {
            seen.Add((group, code));
        }
    }
}

internal sealed class MultiCodeKeys<TValue>(MultiValueColumn<TValue> column) : DistinctSource
{
    public override object Identity => column;

    public override void AddTo(int row, int group, HashSet<(int Group, long Key)> seen)
    {
        foreach (int code in column.CodesOf(row))
        {
            seen.Add((group, code));
        }
    }
}

/// <summary>Raw values as keys: a range facet's doubles by their bits, a date facet's UTC ticks.</summary>
internal sealed class RawKeys(object identity, Func<int, long?> keyAt) : DistinctSource
{
    public override object Identity => identity;

    public override void AddTo(int row, int group, HashSet<(int Group, long Key)> seen)
    {
        if (keyAt(row) is long key)
        {
            seen.Add((group, key));
        }
    }
}
