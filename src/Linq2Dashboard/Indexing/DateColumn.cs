namespace Linq2Dashboard.Indexing;

/// <summary>
/// Column for a date facet (design §3.3). Every row stores the instant as UTC ticks, with
/// <see cref="long.MinValue"/> where null, and a bucket code for the calendar period the instant
/// falls in when viewed in the facet's time zone: 0 for null, 1..B for the periods present in the
/// dataset in chronological order. Immutable once built.
/// </summary>
internal sealed class DateColumn
{
    private const long NullTicks = long.MinValue;

    private readonly long[] _utcTicks;
    private readonly int[] _bucketCodes;
    private readonly long[] _bucketStarts;
    private readonly int[] _totalCounts;

    private DateColumn(
        long[] utcTicks, RowSet nulls, int[] bucketCodes, long[] bucketStarts, int[] totalCounts,
        TimeZoneInfo zone, DateGranularity granularity)
    {
        _utcTicks = utcTicks;
        Nulls = nulls;
        _bucketCodes = bucketCodes;
        _bucketStarts = bucketStarts;
        _totalCounts = totalCounts;
        Zone = zone;
        Granularity = granularity;
    }

    public int RowCount => _utcTicks.Length;

    public TimeZoneInfo Zone { get; }

    public DateGranularity Granularity { get; }

    /// <summary>Rows whose value is null.</summary>
    public RowSet Nulls { get; }

    public int NullCount => Nulls.Count;

    public bool HasNulls => NullCount > 0;

    /// <summary>Number of periods present in the dataset, B. Valid bucket codes are 0..B.</summary>
    public int BucketCount => _bucketStarts.Length;

    public ReadOnlySpan<long> UtcTicks => _utcTicks;

    public ReadOnlySpan<int> BucketCodes => _bucketCodes;

    /// <summary>Total count per bucket code, index 0 being null.</summary>
    public ReadOnlySpan<int> TotalCounts => _totalCounts;

    /// <summary>Start of bucket <paramref name="index"/> (0-based) as a local date-time in the facet zone.</summary>
    public DateTime BucketStart(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, BucketCount);
        return new DateTime(_bucketStarts[index], DateTimeKind.Unspecified);
    }

    /// <summary>
    /// The half-open instant interval <c>[From, To)</c> covered by bucket <paramref name="index"/>.
    /// This is what a bucket click turns into as a selection (design §2.2).
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset To) BucketInterval(int index)
    {
        DateTime start = BucketStart(index);
        return (ToInstant(start), ToInstant(NextPeriodStart(start, Granularity)));
    }

    public static DateColumn Build(int rowCount, RowReader<DateTimeOffset> read, TimeZoneInfo zone, DateGranularity granularity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(zone);
        if (!Enum.IsDefined(granularity))
        {
            throw new ArgumentOutOfRangeException(nameof(granularity));
        }

        var utcTicks = new long[rowCount];
        var periodStarts = new long[rowCount];
        var nulls = new RowSetBuilder(rowCount);
        var distinctStarts = new HashSet<long>();

        for (int row = 0; row < rowCount; row++)
        {
            if (!read(row, out DateTimeOffset instant))
            {
                utcTicks[row] = NullTicks;
                nulls.Set(row);
                continue;
            }

            utcTicks[row] = instant.UtcTicks;
            DateTime local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
            long start = PeriodStart(local, granularity).Ticks;
            periodStarts[row] = start;
            distinctStarts.Add(start);
        }

        long[] bucketStarts = distinctStarts.ToArray();
        Array.Sort(bucketStarts);
        var codeOf = new Dictionary<long, int>(bucketStarts.Length);
        for (int i = 0; i < bucketStarts.Length; i++)
        {
            codeOf[bucketStarts[i]] = i + 1;
        }

        var codes = new int[rowCount];
        for (int row = 0; row < rowCount; row++)
        {
            codes[row] = utcTicks[row] == NullTicks ? 0 : codeOf[periodStarts[row]];
        }

        int[] totals = CodeColumn.Totals(codes, bucketStarts.Length + 1);
        return new DateColumn(utcTicks, nulls.Build(), codes, bucketStarts, totals, zone, granularity);
    }

    /// <summary>
    /// Rows whose instant lies in <c>[from, to)</c> (design §2.2, §4.1). A null bound is unbounded.
    /// Null rows are excluded unless <paramref name="includeNull"/> (concept §4.8).
    /// </summary>
    public RowSet RowsInInterval(DateTimeOffset? from, DateTimeOffset? to, bool includeNull = false)
    {
        if (from is DateTimeOffset f && to is DateTimeOffset t && f > t)
        {
            throw new ArgumentException($"Interval start {f:O} is after its end {t:O}.", nameof(from));
        }

        // NullTicks is long.MinValue, so an unbounded start must still exclude nulls: start at MinValue + 1.
        long low = from?.UtcTicks ?? NullTicks + 1;
        long high = to?.UtcTicks ?? long.MaxValue;
        bool highInclusive = to is null;

        var builder = new RowSetBuilder(RowCount);
        ReadOnlySpan<long> ticks = _utcTicks;
        for (int row = 0; row < ticks.Length; row++)
        {
            long value = ticks[row];
            if (value >= low && (highInclusive ? value <= high : value < high))
            {
                builder.Set(row);
            }
        }

        RowSet result = builder.Build();
        return includeNull ? result.Or(Nulls) : result;
    }

    /// <summary>Filtered counts per bucket code over the context (design §4.5). Length B + 1; index 0 is null.</summary>
    public void CountInto(RowSet context, Span<int> counts) =>
        CodeColumn.CountInto(_bucketCodes, _totalCounts, context, counts);

    /// <summary>Converts a local date-time in the facet zone to an instant, using the zone's offset at that time.</summary>
    public DateTimeOffset ToInstant(DateTime local) =>
        new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone.GetUtcOffset(local));

    /// <summary>Start of the period containing <paramref name="local"/>. ISO weeks start on Monday.</summary>
    internal static DateTime PeriodStart(DateTime local, DateGranularity granularity)
    {
        DateTime day = local.Date;
        return granularity switch
        {
            DateGranularity.Year => new DateTime(day.Year, 1, 1),
            DateGranularity.Month => new DateTime(day.Year, day.Month, 1),
            DateGranularity.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            DateGranularity.Day => day,
            _ => throw new ArgumentOutOfRangeException(nameof(granularity)),
        };
    }

    /// <summary>Start of the period after the one starting at <paramref name="periodStart"/>.</summary>
    internal static DateTime NextPeriodStart(DateTime periodStart, DateGranularity granularity) =>
        granularity switch
        {
            DateGranularity.Year => periodStart.AddYears(1),
            DateGranularity.Month => periodStart.AddMonths(1),
            DateGranularity.Week => periodStart.AddDays(7),
            DateGranularity.Day => periodStart.AddDays(1),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity)),
        };
}
