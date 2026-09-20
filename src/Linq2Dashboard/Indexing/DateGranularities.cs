namespace Linq2Dashboard.Indexing;

/// <summary>
/// Picks a calendar period for a date facet whose definition names none (concept §5, design §3.3):
/// the finest of day, week, month, quarter and year that lays at most about <c>maxPeriods</c>
/// periods over the body of the data. The body is the 2nd to 98th percentile of the non-null
/// instants, read from a stride sample of at most <see cref="MaxSample"/> values so a large column
/// costs one small sort, and so a stray row from decades ago does not force years on everything.
/// The count is estimated from the span in calendar days, so a sparse column is measured by the
/// distance its bars cover, not by how many are lit.
/// </summary>
internal static class DateGranularities
{
    /// <summary>The default target when the definition gives none.</summary>
    public const int DefaultMaxPeriods = 30;

    private const int MaxSample = 100_000;
    private const double LowPercentile = 0.02;
    private const double HighPercentile = 0.98;
    private const double DaysPerYear = 365.2425;

    private static readonly (DateGranularity Granularity, double Days)[] Ladder =
    [
        (DateGranularity.Day, 1),
        (DateGranularity.Week, 7),
        (DateGranularity.Month, DaysPerYear / 12),
        (DateGranularity.Quarter, DaysPerYear / 4),
        (DateGranularity.Year, DaysPerYear),
    ];

    /// <summary>
    /// The granularity for <paramref name="utcTicks"/>, <paramref name="nullTicks"/> marking null,
    /// when viewed in <paramref name="zone"/>. A column with no values gets <see cref="DateGranularity.Month"/>.
    /// </summary>
    public static DateGranularity Auto(ReadOnlySpan<long> utcTicks, long nullTicks, TimeZoneInfo zone, int maxPeriods)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPeriods, 1);

        int nonNull = 0;
        foreach (long ticks in utcTicks)
        {
            if (ticks != nullTicks)
            {
                nonNull++;
            }
        }

        if (nonNull == 0)
        {
            return DateGranularity.Month;
        }

        int stride = Math.Max(1, (nonNull + MaxSample - 1) / MaxSample);
        var sample = new long[(nonNull + stride - 1) / stride];
        int seen = 0;
        int taken = 0;
        foreach (long ticks in utcTicks)
        {
            if (ticks == nullTicks)
            {
                continue;
            }

            if (seen % stride == 0)
            {
                sample[taken++] = ticks;
            }

            seen++;
        }

        Array.Sort(sample, 0, taken);
        long low = sample[(int)Math.Floor(LowPercentile * (taken - 1))];
        long high = sample[(int)Math.Ceiling(HighPercentile * (taken - 1))];
        DateTime lowDay = TimeZoneInfo.ConvertTime(new DateTimeOffset(low, TimeSpan.Zero), zone).Date;
        DateTime highDay = TimeZoneInfo.ConvertTime(new DateTimeOffset(high, TimeSpan.Zero), zone).Date;
        double spanDays = (highDay - lowDay).TotalDays + 1;

        foreach ((DateGranularity granularity, double days) in Ladder)
        {
            if (spanDays / days <= maxPeriods)
            {
                return granularity;
            }
        }

        return DateGranularity.Year;
    }
}
