using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Tests.Indexing;

/// <summary>Concept §5: a date facet without a named period takes the finest one that lays at most about N periods over the body of the data.</summary>
public class DateGranularitiesTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>One row per day for <paramref name="days"/> days, plus a null.</summary>
    private static DateTimeOffset?[] Daily(int days) =>
        [.. Enumerable.Range(0, days).Select(d => (DateTimeOffset?)Start.AddDays(d)), null];

    private static DateGranularity Resolve(DateTimeOffset?[] values, int maxPeriods = DateGranularities.DefaultMaxPeriods) =>
        DateColumn.Build(values.Length, (int row, out DateTimeOffset value) =>
        {
            value = values[row].GetValueOrDefault();
            return values[row].HasValue;
        }, Stockholm, null, maxPeriods).Granularity;

    [Theory]
    [InlineData(1, DateGranularity.Day)]
    [InlineData(30, DateGranularity.Day)]
    [InlineData(31, DateGranularity.Week)]
    [InlineData(210, DateGranularity.Week)]
    [InlineData(211, DateGranularity.Month)]
    [InlineData(2 * 365, DateGranularity.Month)]
    [InlineData(3 * 365, DateGranularity.Quarter)]
    [InlineData(7 * 365, DateGranularity.Quarter)]
    [InlineData(8 * 365, DateGranularity.Year)]
    [InlineData(100 * 365, DateGranularity.Year)]
    public void The_finest_period_within_thirty_over_the_span_is_chosen(int days, DateGranularity expected)
    {
        // A span within the sample's percentiles: with fewer than 50 values the 2nd and 98th percentile are the ends.
        var values = days <= 30 ? Daily(days) : Spread(days);
        Assert.Equal(expected, Resolve(values));
    }

    /// <summary>Forty values spread evenly over <paramref name="days"/> days, so the percentiles are the first and last.</summary>
    private static DateTimeOffset?[] Spread(int days) =>
        [.. Enumerable.Range(0, 40).Select(i => (DateTimeOffset?)Start.AddDays((days - 1) * i / 39.0))];

    [Fact]
    public void The_ceiling_moves_the_choice()
    {
        var twoYears = Daily(730);
        Assert.Equal(DateGranularity.Month, Resolve(twoYears));
        Assert.Equal(DateGranularity.Quarter, Resolve(twoYears, 12));
        Assert.Equal(DateGranularity.Year, Resolve(twoYears, 4));
        Assert.Equal(DateGranularity.Week, Resolve(twoYears, 200));
        Assert.Equal(DateGranularity.Day, Resolve(twoYears, 1000));
    }

    [Fact]
    public void A_stray_date_far_away_does_not_decide()
    {
        // 730 daily values over two years and one from 1990: the percentiles trim it, so months, not years.
        DateTimeOffset?[] values = [.. Daily(730), new DateTimeOffset(1990, 6, 1, 0, 0, 0, TimeSpan.Zero)];
        Assert.Equal(DateGranularity.Month, Resolve(values));

        // Twenty stray rows among 730 are more than two per cent, so they count.
        DateTimeOffset?[] many = [.. Daily(730), .. Enumerable.Range(0, 20).Select(i => (DateTimeOffset?)new DateTimeOffset(1990, 6, 1 + i, 0, 0, 0, TimeSpan.Zero))];
        Assert.Equal(DateGranularity.Year, Resolve(many));
    }

    [Fact]
    public void No_dates_give_months_and_the_span_is_measured_in_the_facet_zone()
    {
        Assert.Equal(DateGranularity.Month, Resolve([]));
        Assert.Equal(DateGranularity.Month, Resolve([null, null]));

        // 30 January 23:30 UTC to 1 March 22:30 UTC spans 31 days in UTC but 30 in Stockholm, where they are 31 January 00:30 and 1 March 23:30.
        DateTimeOffset?[] edge = [new DateTimeOffset(2026, 1, 30, 23, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 22, 30, 0, TimeSpan.Zero)];
        Assert.Equal(DateGranularity.Day, Resolve(edge));
        Assert.Equal(DateGranularity.Week, DateColumn.Build(2, (int row, out DateTimeOffset value) =>
        {
            value = edge[row]!.Value;
            return true;
        }, TimeZoneInfo.Utc, null, 30).Granularity);
    }

    [Fact]
    public void A_large_column_is_sampled_deterministically()
    {
        var random = new Random(5);
        var values = Enumerable.Range(0, 400_000)
            .Select(_ => random.Next(10) == 0 ? null : (DateTimeOffset?)Start.AddMinutes(random.Next(60 * 24 * 1500)))
            .ToArray();

        Assert.Equal(DateGranularity.Quarter, Resolve(values));
        Assert.Equal(Resolve(values), Resolve(values));
    }
}
