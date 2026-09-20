using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Tests.Indexing;

public class DateColumnTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset Instant(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    private static DateColumn Build(DateTimeOffset?[] values, TimeZoneInfo zone, DateGranularity granularity) =>
        DateColumn.Build(values.Length, (int row, out DateTimeOffset value) =>
        {
            value = values[row].GetValueOrDefault();
            return values[row].HasValue;
        }, zone, granularity);

    // Row:  0                         1                       2     3                         4                         5
    private static readonly DateTimeOffset?[] Orders =
    [
        Instant("2026-01-15T10:00:00Z"),
        Instant("2026-03-01T23:30:00Z"),   // 2026-03-02 00:30 in Stockholm (+01:00)
        null,
        Instant("2026-03-31T22:30:00Z"),   // 2026-04-01 00:30 in Stockholm (+02:00, DST)
        Instant("2025-12-31T23:30:00Z"),   // 2026-01-01 00:30 in Stockholm
        Instant("2026-03-10T12:00:00Z"),
    ];

    [Theory]
    [InlineData("2026-03-15T13:45:00", DateGranularity.Year, "2026-01-01T00:00:00")]
    [InlineData("2026-03-15T13:45:00", DateGranularity.Quarter, "2026-01-01T00:00:00")]
    [InlineData("2026-04-01T00:00:00", DateGranularity.Quarter, "2026-04-01T00:00:00")] // first day of Q2 stays
    [InlineData("2026-12-31T23:59:59", DateGranularity.Quarter, "2026-10-01T00:00:00")]
    [InlineData("2026-03-15T13:45:00", DateGranularity.Month, "2026-03-01T00:00:00")]
    [InlineData("2026-03-15T13:45:00", DateGranularity.Day, "2026-03-15T00:00:00")]
    [InlineData("2026-03-15T13:45:00", DateGranularity.Week, "2026-03-09T00:00:00")] // Sunday → previous Monday
    [InlineData("2026-03-09T00:00:00", DateGranularity.Week, "2026-03-09T00:00:00")] // Monday stays
    [InlineData("2026-03-14T23:59:59", DateGranularity.Week, "2026-03-09T00:00:00")] // Saturday
    [InlineData("2026-01-01T00:00:00", DateGranularity.Week, "2025-12-29T00:00:00")] // week spans year boundary
    public void PeriodStart_truncates_to_the_calendar_period(string local, DateGranularity granularity, string expected)
    {
        var result = DateColumn.PeriodStart(DateTime.Parse(local, System.Globalization.CultureInfo.InvariantCulture), granularity);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result);
        Assert.Equal(DateTimeKind.Unspecified, result.Kind);
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00", DateGranularity.Year, "2027-01-01T00:00:00")]
    [InlineData("2026-01-01T00:00:00", DateGranularity.Quarter, "2026-04-01T00:00:00")]
    [InlineData("2026-10-01T00:00:00", DateGranularity.Quarter, "2027-01-01T00:00:00")]
    [InlineData("2026-01-01T00:00:00", DateGranularity.Month, "2026-02-01T00:00:00")]
    [InlineData("2026-12-01T00:00:00", DateGranularity.Month, "2027-01-01T00:00:00")]
    [InlineData("2026-03-09T00:00:00", DateGranularity.Week, "2026-03-16T00:00:00")]
    [InlineData("2026-02-28T00:00:00", DateGranularity.Day, "2026-03-01T00:00:00")]
    public void NextPeriodStart_advances_one_period(string start, DateGranularity granularity, string expected)
    {
        var result = DateColumn.NextPeriodStart(DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture), granularity);

        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result);
    }

    [Fact]
    public void Instants_are_stored_as_utc_ticks_with_MinValue_for_null()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);

        Assert.Equal(6, column.RowCount);
        Assert.Equal(Orders[0]!.Value.UtcTicks, column.UtcTicks[0]);
        Assert.Equal(long.MinValue, column.UtcTicks[2]);
        Assert.Equal([2], column.Nulls.Rows());
        Assert.Equal(1, column.NullCount);
    }

    [Fact]
    public void Buckets_are_periods_present_in_the_dataset_in_chronological_order()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);

        Assert.Equal(3, column.BucketCount);
        Assert.Equal(new DateTime(2025, 12, 1), column.BucketStart(0));
        Assert.Equal(new DateTime(2026, 1, 1), column.BucketStart(1));
        Assert.Equal(new DateTime(2026, 3, 1), column.BucketStart(2));
        //                      Jan  Mar  null Mar  Dec  Mar
        Assert.Equal([2, 3, 0, 3, 1, 3], column.BucketCodes.ToArray());
        Assert.Equal([1, 1, 1, 3], column.TotalCounts.ToArray());
    }

    [Fact]
    public void Bucketing_happens_in_the_facet_time_zone()
    {
        var column = Build(Orders, Stockholm, DateGranularity.Month);

        // In Stockholm row 4 is January, row 3 is April; December disappears and April appears.
        Assert.Equal(3, column.BucketCount);
        Assert.Equal(new DateTime(2026, 1, 1), column.BucketStart(0));
        Assert.Equal(new DateTime(2026, 3, 1), column.BucketStart(1));
        Assert.Equal(new DateTime(2026, 4, 1), column.BucketStart(2));
        //                      Jan  Mar  null Apr  Jan  Mar
        Assert.Equal([1, 2, 0, 3, 1, 2], column.BucketCodes.ToArray());
    }

    [Fact]
    public void Day_buckets_follow_the_zone_at_midnight()
    {
        var column = Build(Orders, Stockholm, DateGranularity.Day);

        Assert.Equal(new DateTime(2026, 3, 2), column.BucketStart(2));   // row 1: 23:30Z on 1 March
        Assert.Equal(new DateTime(2026, 4, 1), column.BucketStart(4));   // row 3: 22:30Z on 31 March
    }

    [Fact]
    public void BucketInterval_is_half_open_and_carries_the_zone_offset_at_each_end()
    {
        var column = Build(Orders, Stockholm, DateGranularity.Month);

        (DateTimeOffset from, DateTimeOffset to) = column.BucketInterval(1); // March 2026, DST starts 29 March

        Assert.Equal(Instant("2026-03-01T00:00:00+01:00"), from);
        Assert.Equal(TimeSpan.FromHours(1), from.Offset);
        Assert.Equal(Instant("2026-04-01T00:00:00+02:00"), to);
        Assert.Equal(TimeSpan.FromHours(2), to.Offset);
    }

    [Fact]
    public void Bucket_interval_used_as_a_selection_reproduces_the_bucket()
    {
        var column = Build(Orders, Stockholm, DateGranularity.Month);

        for (int i = 0; i < column.BucketCount; i++)
        {
            (DateTimeOffset from, DateTimeOffset to) = column.BucketInterval(i);
            var expected = Enumerable.Range(0, column.RowCount).Where(r => column.BucketCodes[r] == i + 1);

            Assert.Equal(expected, column.RowsInInterval(from, to).Rows());
        }
    }

    [Fact]
    public void Interval_is_half_open_on_instants()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);

        Assert.Equal([1, 5], column.RowsInInterval(Instant("2026-03-01T23:30:00Z"), Instant("2026-03-31T22:30:00Z")).Rows());
        Assert.True(column.RowsInInterval(Instant("2026-03-10T12:00:00Z"), Instant("2026-03-10T12:00:00Z")).IsEmpty);
    }

    [Fact]
    public void Unbounded_sides_take_everything_on_that_side_but_never_null()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);

        Assert.Equal([1, 3, 5], column.RowsInInterval(Instant("2026-03-01T00:00:00Z"), null).Rows());
        Assert.Equal([0, 4], column.RowsInInterval(null, Instant("2026-03-01T00:00:00Z")).Rows());
        Assert.Equal([0, 1, 3, 4, 5], column.RowsInInterval(null, null).Rows());
    }

    [Fact]
    public void IncludeNull_adds_the_null_rows()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);

        Assert.Equal([0, 2, 4], column.RowsInInterval(null, Instant("2026-03-01T00:00:00Z"), includeNull: true).Rows());
    }

    [Fact]
    public void Interval_with_start_after_end_is_rejected()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);

        Assert.Throws<ArgumentException>(() =>
            column.RowsInInterval(Instant("2026-04-01T00:00:00Z"), Instant("2026-03-01T00:00:00Z")));
    }

    [Fact]
    public void CountInto_counts_periods_over_the_context()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);
        var counts = new int[4];

        column.CountInto(RowSet.FromRows(6, [1, 2, 4]), counts); // Mar, null, Dec

        Assert.Equal([1, 1, 0, 1], counts);
    }

    [Fact]
    public void CountInto_over_full_context_equals_totals()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);
        var counts = new int[4];

        column.CountInto(RowSet.Full(6), counts);

        Assert.Equal(column.TotalCounts.ToArray(), counts);
    }

    [Fact]
    public void Empty_and_all_null_datasets_build()
    {
        var empty = Build([], Utc, DateGranularity.Day);
        var nulls = Build([null, null], Utc, DateGranularity.Day);

        Assert.Equal(0, empty.BucketCount);
        Assert.Equal([0], empty.TotalCounts.ToArray());
        Assert.Equal(0, nulls.BucketCount);
        Assert.Equal([2], nulls.TotalCounts.ToArray());
        Assert.True(nulls.RowsInInterval(null, null).IsEmpty);
        Assert.Equal(2, nulls.RowsInInterval(null, null, includeNull: true).Count);
    }

    [Fact]
    public void BucketStart_rejects_index_out_of_range()
    {
        var column = Build(Orders, Utc, DateGranularity.Month);

        Assert.Throws<ArgumentOutOfRangeException>(() => column.BucketStart(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => column.BucketStart(3));
    }

    [Fact]
    public void Undefined_granularity_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(Orders, Utc, (DateGranularity)99));
    }

    [Fact]
    public void Week_buckets_across_a_year_boundary_match_brute_force()
    {
        var random = new Random(11);
        var start = Instant("2025-12-01T00:00:00Z");
        var values = Enumerable.Range(0, 2000)
            .Select(_ => random.Next(8) == 0 ? null : (DateTimeOffset?)start.AddMinutes(random.Next(60 * 24 * 90)))
            .ToArray();
        var column = Build(values, Stockholm, DateGranularity.Week);

        for (int row = 0; row < values.Length; row++)
        {
            if (values[row] is not DateTimeOffset instant)
            {
                Assert.Equal(0, column.BucketCodes[row]);
                continue;
            }

            DateTime local = TimeZoneInfo.ConvertTime(instant, Stockholm).DateTime.Date;
            DateTime monday = local.AddDays(-(((int)local.DayOfWeek + 6) % 7));
            Assert.Equal(monday, column.BucketStart(column.BucketCodes[row] - 1));
        }

        for (int i = 1; i < column.BucketCount; i++)
        {
            Assert.Equal(7, (column.BucketStart(i) - column.BucketStart(i - 1)).TotalDays);
        }
    }
}
