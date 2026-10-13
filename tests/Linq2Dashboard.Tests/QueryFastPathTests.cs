namespace Linq2Dashboard.Tests;

/// <summary>
/// The query's fast paths agree with a plain LINQ answer (design §10.3): distinct counts as bits per group, and periods
/// converted once per quarter hour, in zones with half and three-quarter hour offsets and across daylight saving.
/// </summary>
public class QueryFastPathTests
{
    private static Order[] Generate(int count, int seed)
    {
        var random = new Random(seed);
        string[] countries = ["SE", "NO", "DK", "FI", "se"];
        string[] statuses = ["Open", "Closed", "Pending"];
        var start = new DateTime(2025, 1, 1);
        return Enumerable.Range(1, count).Select(id =>
        {
            DateTime local = start.AddMinutes(random.Next(2 * 365 * 24 * 60));
            DateTimeOffset? shipped = random.Next(10) == 0 ? null : new DateTimeOffset(local.AddHours(random.Next(48)), TimeSpan.FromMinutes(random.Next(-8, 9) * 45));
            return new Order(
                id,
                random.Next(12) == 0 ? null : countries[random.Next(countries.Length)],
                statuses[random.Next(statuses.Length)],
                random.Next(2) == 0, null,
                random.Next(1, 300),
                null,
                random.Next(1, 6),
                local,
                shipped,
                DateOnly.FromDateTime(local).AddDays(random.Next(30)),
                random.Next(2) == 0 ? OrderKind.Web : OrderKind.Store,
                null)
            {
                Tags = Enumerable.Range(0, random.Next(4)).Select(_ => (string?)$"t{random.Next(8)}").ToArray(),
            };
        }).ToArray();
    }

    private static Dictionary<(object?, object?), int> Distinct(Order[] orders, Func<Order, IEnumerable<object?>> keys) =>
        orders
            .GroupBy(o => ((object?)o.Status, (object?)o.Kind))
            .ToDictionary(g => g.Key, g => g.SelectMany(keys).Where(k => k is not null).Select(k => k is string s ? s.ToUpperInvariant() : k).Distinct().Count());

    [Fact]
    public void Distinct_per_group_as_bits_agrees_with_linq()
    {
        Order[] orders = Generate(5_000, 7);
        var dashboard = Dashboard.Create(orders, b =>
        {
            b.ValueFacet(x => x.Status);
            b.ValueFacet(x => x.Kind);
            b.ValueFacet(x => x.Country);
            b.MultiValueFacet(x => x.Tags);
            b.DistinctMetric("countries", x => x.Country);
        });

        // Facet codes give bits; the property Quantity is coded as it is read and keeps the hash.
        var answer = dashboard.Query(new()
        {
            GroupBy = [Dimension.Of("Status"), Dimension.Of("Kind")],
            Values = [QueryValue.Distinct("Country"), QueryValue.Metric("countries"), QueryValue.Distinct("Tags"), QueryValue.Distinct("Quantity")],
        });

        Dictionary<(object?, object?), double?[]> actual = answer.Rows.ToDictionary(
            r => (((QueryValueCell)r[0]!).Value, ((QueryValueCell)r[1]!).Value),
            r => r.Cells.Skip(2).Select(c => (double?)c).ToArray());
        var countries = Distinct(orders, o => [o.Country]);
        var tags = Distinct(orders, o => o.Tags ?? []);
        var quantities = Distinct(orders, o => [o.Quantity]);
        Assert.Equal(countries.Count, actual.Count);
        foreach (var (key, values) in actual)
        {
            Assert.Equal([countries[key], countries[key], tags[key], quantities[key]], values.Select(v => (int)v!.Value));
        }
    }

    [Fact]
    public void Distinct_per_group_falls_back_to_the_hash_when_the_bits_would_not_fit()
    {
        Order[] orders = Generate(2_000, 11);
        var dashboard = Dashboard.Create(orders, b => b.ValueFacet(x => x.Country));

        // A property dimension has no known number of codes, so the bits cannot be sized and the hash is used.
        var answer = dashboard.Query(new() { GroupBy = [Dimension.Property("Status")], Values = [QueryValue.Distinct("Country")] });

        var expected = orders.GroupBy(o => o.Status).ToDictionary(g => (object)g.Key, g => g.Where(o => o.Country is not null).Select(o => o.Country!.ToUpperInvariant()).Distinct().Count());
        Assert.Equal(expected, answer.Rows.ToDictionary(r => ((QueryValueCell)r[0]!).Value!, r => (int)(double)r[1]!));
    }

    [Theory]
    [InlineData("Europe/Stockholm", DateGranularity.Day)]
    [InlineData("Europe/Stockholm", DateGranularity.Week)]
    [InlineData("Asia/Kolkata", DateGranularity.Day)]
    [InlineData("Asia/Kathmandu", DateGranularity.Day)]
    [InlineData("Australia/Lord_Howe", DateGranularity.Day)]
    [InlineData("America/St_Johns", DateGranularity.Week)]
    public void Periods_converted_per_row_agree_with_linq(string zoneId, DateGranularity period)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        Order[] orders = Generate(20_000, 3);
        var dashboard = Dashboard.Create(orders, b => b.DateFacet(x => x.Shipped).Granularity(DateGranularity.Month).TimeZone(zone));

        var answer = dashboard.Query(new() { GroupBy = [Dimension.Of("Shipped", period)] });

        var expected = orders
            .Where(o => o.Shipped is not null)
            .GroupBy(o => PeriodStart(TimeZoneInfo.ConvertTime(o.Shipped!.Value, zone).DateTime, period))
            .ToDictionary(g => g.Key, g => g.Count());
        var actual = answer.Rows.Where(r => r[0] is not null).ToDictionary(r => ((QueryPeriod)r[0]!).PeriodStart, r => (int)(double)r["Count"]!);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("OrderDate")]
    [InlineData("Shipped")]
    [InlineData("Due")]
    public void A_date_property_by_period_in_utc_agrees_with_linq(string property)
    {
        Order[] orders = Generate(10_000, 5);
        var dashboard = Dashboard.Create(orders, _ => { });

        var answer = dashboard.Query(new() { GroupBy = [Dimension.Of(property, DateGranularity.Day)] });

        Func<Order, DateTime?> utc = property switch
        {
            "OrderDate" => o => o.OrderDate,
            "Shipped" => o => o.Shipped?.UtcDateTime,
            _ => o => o.Due.ToDateTime(TimeOnly.MinValue),
        };
        var expected = orders.Where(o => utc(o) is not null).GroupBy(o => utc(o)!.Value.Date).ToDictionary(g => g.Key, g => g.Count());
        var actual = answer.Rows.Where(r => r[0] is not null).ToDictionary(r => ((QueryPeriod)r[0]!).PeriodStart, r => (int)(double)r["Count"]!);
        Assert.Equal(expected, actual);
    }

    private static DateTime PeriodStart(DateTime local, DateGranularity period) => period switch
    {
        DateGranularity.Day => local.Date,
        DateGranularity.Week => local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)),
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };
}
