namespace Linq2Dashboard.Tests;

public class QueryTests
{
    private static readonly FixedTimeProvider Clock = new(TestData.Instant("2026-03-15T10:00:00Z"));

    // Row: Country Status  Active Amount Discount Quantity OrderDate (Stockholm) Tags
    //  0:  SE      Open    T      100    10       2        15 Jan                urgent, gift
    //  1:  NO      Open    T      250    null     null      3 Feb                gift
    //  2:  null    Closed  F      500    50       1        20 Feb                null
    //  3:  SE      Closed  T      999.5  0        5         1 Mar                Urgent, urgent, b2b
    //  4:  DK      Pending F      1000   null     3        10 Mar                (empty)
    //  5:  se      Open    T      2500   250      null     31 Mar                b2b
    //  6:  null    Pending F      0      null     0         2 Apr                [null]
    //  7:  NO      Open    T      75     5        2        15 Apr                gift, b2b
    private static Dashboard<Order> Build() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.ValueFacet(x => x.Quantity);
            b.BooleanFacet(x => x.IsActive);
            b.MultiValueFacet(x => x.Tags);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm);
            b.TextFacet("search", (x, text) => x.Status.Contains(text, StringComparison.OrdinalIgnoreCase));
            b.CountMetric("orders");
            b.SumMetric("revenue", x => x.Amount);
            b.AverageMetric("avgDiscount", x => x.Discount);
            b.DistinctMetric("countries", x => x.Country);
            b.CalculatedMetric("perOrder", m => m["revenue"] / m["orders"]);
            b.UseTimeProvider(Clock);
        });

    private static readonly Dashboard<Order> Shared = Build();

    private static QueryResult Ask(DashboardQuery<Order> query) => Shared.Query(query);

    private static object? ValueOf(object? cell) => (cell as QueryValueCell)?.Value;

    [Fact]
    public void Query_without_dimensions_gives_one_row_with_the_dashboards_metrics()
    {
        var answer = Ask(new()
        {
            Values = [QueryValue.Metric("orders"), QueryValue.Metric("revenue"), QueryValue.Metric("avgDiscount"), QueryValue.Metric("countries"), QueryValue.Metric("perOrder")],
        });

        var state = Shared.Calculate();
        Assert.Equal(1, answer.Total);
        var row = Assert.Single(answer.Rows);
        Assert.Equal(state.Metrics.Select(m => (object?)m.Value), row.Cells);
    }

    [Fact]
    public void Query_without_dimensions_over_no_rows_still_gives_its_one_row()
    {
        var answer = Ask(new()
        {
            Selections = Selections.Empty.With("Status", ValueSelection.Of("Shipped")),
            Values = [QueryValue.Count(), QueryValue.Metric("revenue")],
        });

        var row = Assert.Single(answer.Rows);
        Assert.Equal([0.0, null], row.Cells);
    }

    [Fact]
    public void Query_without_values_counts_and_puts_null_last()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("Country")] });

        Assert.Equal(["Country", "Count"], answer.Columns.Select(c => c.Key));
        Assert.Equal(
            [("DK", 1.0), ("NO", 2.0), ("SE", 3.0), (null, 2.0)],
            answer.Rows.Select(r => (ValueOf(r["Country"]), (double)r["Count"]!)));
    }

    [Fact]
    public void Query_applies_every_selection_without_own_facet_exclusion()
    {
        var selections = Selections.Empty.With("Country", ValueSelection.Of("SE"));

        var answer = Ask(new() { Selections = selections, GroupBy = [Dimension.Of("Country")] });
        var facet = (ValueFacetState)Shared.Calculate(selections).Facet("Country");

        var row = Assert.Single(answer.Rows);
        Assert.Equal("SE", ValueOf(row["Country"]));
        Assert.Equal(3.0, row["Count"]);
        Assert.Contains(facet.Values, v => Equals(v.Value, "NO") && v.FilteredCount == 2);
    }

    [Fact]
    public void Query_leaves_out_groups_without_rows()
    {
        var answer = Ask(new()
        {
            Selections = Selections.Empty.With("Status", ValueSelection.Of("Pending")),
            GroupBy = [Dimension.Of("Country")],
        });

        Assert.Equal(["DK", null], answer.Rows.Select(r => ValueOf(r["Country"])));
        Assert.Equal(2, answer.Total);
    }

    [Fact]
    public void Query_grouped_by_a_facet_agrees_with_its_filtered_counts()
    {
        Selections[] candidates =
        [
            Selections.Empty,
            Selections.Empty.With("Status", ValueSelection.Of("Open")),
            Selections.Empty.With("IsActive", ValueSelection.Of(true)),
            Selections.Empty.With("Amount", RangeSelection.Between(100, 1000)),
            Selections.Empty.With("Tags", ValueSelection.Of("gift")),
            Selections.Empty.With("OrderDate", DateSelection.Between(TestData.Instant("2026-02-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00"))),
        ];

        var random = new Random(42);
        for (int round = 0; round < 40; round++)
        {
            Selections selections = Selections.Empty;
            foreach (Selections candidate in candidates.Where(_ => random.Next(3) == 0))
            {
                foreach ((string key, Selection selection) in candidate)
                {
                    selections = selections.With(key, selection);
                }
            }

            DashboardState<Order> state = Shared.Calculate(selections);
            foreach (FacetState facet in state.Facets.Where(f => f.Kind != FacetKind.Text && !f.HasSelection))
            {
                var answer = Ask(new() { Selections = selections, GroupBy = [Dimension.Of(facet.Key)] });
                var fromQuery = answer.Rows.ToDictionary(r => GroupKey(r[0]), r => (int)(double)r["Count"]!);
                Assert.Equal(FilteredCounts(facet), fromQuery);
            }
        }
    }

    [Fact]
    public void Query_puts_a_multi_valued_row_in_every_group_it_has_a_value_in()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("Tags")] });

        Assert.Equal(
            [("b2b", 3.0), ("gift", 3.0), ("urgent", 2.0), (null, 3.0)],
            answer.Rows.Select(r => (ValueOf(r["Tags"]), (double)r["Count"]!)));
        Assert.Equal(QueryColumnKind.Value, answer.Column("Tags").Kind);
    }

    [Fact]
    public void Query_range_cells_are_bucket_intervals_that_select_the_same_rows()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("Amount")] });

        Assert.Equal(QueryColumnKind.Interval, answer.Column("Amount").Kind);
        Assert.Equal(
            [new RangeInterval(null, 100, true, false), new RangeInterval(100, 500, true, false), new RangeInterval(500, 1000, true, false), new RangeInterval(1000, null, true, false)],
            answer.Rows.Select(r => (RangeInterval)r["Amount"]!));

        foreach (QueryRow row in answer.Rows)
        {
            var selections = Selections.Empty.ToggleInterval("Amount", (RangeInterval)row["Amount"]!);
            Assert.Equal((double)row["Count"]!, Shared.Calculate(selections).MatchingCount);
        }
    }

    [Fact]
    public void Query_date_cells_are_periods_that_select_the_same_rows()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("OrderDate")] });

        QueryColumn column = answer.Column("OrderDate");
        Assert.Equal((QueryColumnKind.Period, DateGranularity.Month), (column.Kind, column.Granularity));
        var periods = answer.Rows.Select(r => (QueryPeriod)r["OrderDate"]!).ToArray();
        Assert.Equal([1, 2, 3, 4], periods.Select(p => p.PeriodStart.Month));
        Assert.Equal(TestData.Instant("2026-03-01T00:00:00+01:00"), periods[2].Interval.From);
        Assert.Equal(TestData.Instant("2026-04-01T00:00:00+02:00"), periods[2].Interval.To);
        Assert.Equal([1.0, 2.0, 3.0, 2.0], answer.Rows.Select(r => (double)r["Count"]!));

        foreach (QueryRow row in answer.Rows)
        {
            var selections = Selections.Empty.ToggleInterval("OrderDate", ((QueryPeriod)row["OrderDate"]!).Interval);
            Assert.Equal((double)row["Count"]!, Shared.Calculate(selections).MatchingCount);
        }
    }

    [Fact]
    public void Query_groups_by_two_dimensions_in_their_natural_order()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("Country"), Dimension.Of("Status")] });

        Assert.Equal(
            [("DK", "Pending", 1.0), ("NO", "Open", 2.0), ("SE", "Closed", 1.0), ("SE", "Open", 2.0), (null, "Closed", 1.0), (null, "Pending", 1.0)],
            answer.Rows.Select(r => (ValueOf(r["Country"]), ValueOf(r["Status"]), (double)r["Count"]!)));
        Assert.Equal(6, answer.Total);
    }

    [Fact]
    public void Query_orders_by_a_value_with_no_value_last_in_both_directions()
    {
        QueryResult Ordered(QueryOrder order) => Ask(new()
        {
            GroupBy = [Dimension.Of("Country")],
            Values = [QueryValue.Metric("avgDiscount")],
            OrderBy = order,
        });

        // Average discount: SE (10, 0, 250) 86.67, null (50) 50, NO (5) 5, DK none.
        Assert.Equal(["SE", null, "NO", "DK"], Ordered(QueryOrder.Descending("avgDiscount")).Rows.Select(r => ValueOf(r[0])));
        Assert.Equal(["NO", null, "SE", "DK"], Ordered(QueryOrder.Ascending("avgDiscount")).Rows.Select(r => ValueOf(r[0])));
        Assert.Null(Ordered(QueryOrder.Ascending("avgDiscount")).Rows[^1]["avgDiscount"]);
    }

    [Fact]
    public void Query_orders_by_a_dimension_with_null_last_in_both_directions()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("Country")], OrderBy = QueryOrder.Descending("Country") });

        Assert.Equal(["SE", "NO", "DK", null], answer.Rows.Select(r => ValueOf(r[0])));
    }

    [Fact]
    public void Query_take_cuts_the_ordered_groups_and_reports_how_many_there_were()
    {
        var answer = Ask(new()
        {
            GroupBy = [Dimension.Of("Country")],
            Values = [QueryValue.Metric("revenue")],
            OrderBy = QueryOrder.Descending("revenue"),
            Take = 2,
        });

        Assert.Equal([("SE", 3599.5), ("DK", 1000.0)], answer.Rows.Select(r => (ValueOf(r[0]), (double)r["revenue"]!)));
        Assert.Equal(4, answer.Total);
        Assert.True(answer.IsCut);

        var none = Ask(new() { GroupBy = [Dimension.Of("Country")], Take = 0 });
        Assert.Empty(none.Rows);
        Assert.Equal(4, none.Total);
    }

    [Fact]
    public void Query_evaluates_a_calculated_metric_per_group()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("Country")], Values = [QueryValue.Metric("perOrder")] });

        Assert.Equal(
            [("DK", 1000.0), ("NO", 162.5), ("SE", 3599.5 / 3), (null, 250.0)],
            answer.Rows.Select(r => (ValueOf(r[0]), (double)r["perOrder"]!)));
    }

    [Fact]
    public void Query_counts_distinct_values_per_group_with_the_facets_equality()
    {
        var answer = Ask(new()
        {
            GroupBy = [Dimension.Of("Status")],
            Values = [QueryValue.Metric("countries"), QueryValue.Distinct("Country")],
        });

        // Open has SE, NO and "se", which is SE.
        Assert.Equal(
            [("Closed", 1.0, 1.0), ("Open", 2.0, 2.0), ("Pending", 1.0, 1.0)],
            answer.Rows.Select(r => (ValueOf(r[0]), (double)r["countries"]!, (double)r["Distinct(Country)"]!)));
    }

    [Fact]
    public void Query_aggregates_a_range_facet_and_a_numeric_value_facet()
    {
        var answer = Ask(new()
        {
            GroupBy = [Dimension.Of("Status")],
            Values = [QueryValue.Sum("Amount"), QueryValue.Average("Quantity"), QueryValue.Max("Quantity"), QueryValue.Min("Amount")],
        });

        Assert.Equal(
            [("Closed", 1499.5, 3.0, 5.0, 500.0), ("Open", 2925.0, 2.0, 2.0, 75.0), ("Pending", 1000.0, 1.5, 3.0, 0.0)],
            answer.Rows.Select(r => (ValueOf(r[0]), (double)r[1]!, (double)r[2]!, (double)r[3]!, (double)r[4]!)));
    }

    [Fact]
    public void Query_columns_say_what_they_are()
    {
        var answer = Ask(new()
        {
            GroupBy = [Dimension.Of("OrderDate").As("month")],
            Values = [QueryValue.Metric("revenue"), QueryValue.Count(), QueryValue.Average("Amount").As("avg")],
        });

        Assert.Equal(
            [
                new QueryColumn("month", "OrderDate", QueryColumnRole.Dimension, QueryColumnKind.Period) { Source = "OrderDate", Granularity = DateGranularity.Month },
                new QueryColumn("revenue", "revenue", QueryColumnRole.Value, QueryColumnKind.Number) { Source = "revenue", Aggregation = Aggregation.Sum },
                new QueryColumn("Count", "Count", QueryColumnRole.Value, QueryColumnKind.Number) { Aggregation = Aggregation.Count },
                new QueryColumn("avg", "avg", QueryColumnRole.Value, QueryColumnKind.Number) { Source = "Amount", Aggregation = Aggregation.Average },
            ],
            answer.Columns);
    }

    [Fact]
    public void Query_cells_carry_a_value_facets_labels()
    {
        var dashboard = Dashboard.Create(TestData.Orders(), b => b.ValueFacet(x => x.Id).Label(x => $"Order {x.Id}"));

        var answer = dashboard.Query(new() { GroupBy = [Dimension.Of("Id")], Take = 2 });

        Assert.Equal([new QueryValueCell(1, "Order 1"), new QueryValueCell(2, "Order 2")], answer.Rows.Select(r => r["Id"]));
    }

    [Fact]
    public void Query_on_a_scoped_dashboard_asks_about_the_scope()
    {
        var active = Shared.ScopeTo(x => x.IsActive);

        var answer = active.Query(new() { GroupBy = [Dimension.Of("Country")] });

        Assert.Equal([("NO", 2.0), ("SE", 3.0)], answer.Rows.Select(r => (ValueOf(r[0]), (double)r["Count"]!)));
    }

    [Fact]
    public void Query_leaves_nothing_in_the_state_cache()
    {
        var dashboard = Build();

        dashboard.Query(new() { Selections = Selections.Empty.With("Status", ValueSelection.Of("Open")), GroupBy = [Dimension.Of("Country")] });

        Assert.Equal(0, dashboard.CachedStateCount);
    }

    [Fact]
    public void Query_answers_the_same_from_a_cached_state()
    {
        var dashboard = Build();
        var query = new DashboardQuery<Order> { Selections = Selections.Empty.With("Status", ValueSelection.Of("Open")), GroupBy = [Dimension.Of("Country")] };
        var cold = dashboard.Query(query);

        dashboard.Calculate(query.Selections);
        var warm = dashboard.Query(query);

        Assert.Equal(cold.Rows.Select(r => r.Cells), warm.Rows.Select(r => r.Cells));
    }

    [Fact]
    public void Query_throws_when_cancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => Shared.Query(new() { GroupBy = [Dimension.Of("Country")] }, cancelled.Token));
    }

    [Theory]
    [MemberData(nameof(MistakeNames))]
    public void Query_is_strict_and_says_what_is_wrong(string mistake)
    {
        (string expected, DashboardQuery<Order> query) = Mistakes[mistake];

        var error = Assert.Throws<ArgumentException>(() => Ask(query));
        Assert.Contains(expected, error.Message);
    }

    public static TheoryData<string> MistakeNames() => new(Mistakes.Keys);

    private static readonly Dictionary<string, (string Expected, DashboardQuery<Order> Query)> Mistakes = new()
    {
        ["dimension in the wrong case"] = ("Did you mean 'Country'?", new() { GroupBy = [Dimension.Of("country")] }),
        ["unknown dimension"] = ("'Customer' is not a facet of this dashboard. Facets: Country, Status", new() { GroupBy = [Dimension.Of("Customer")] }),
        ["metric as dimension"] = ("'revenue' is a metric", new() { GroupBy = [Dimension.Of("revenue")] }),
        ["three dimensions"] = ("at most two dimensions", new() { GroupBy = [Dimension.Of("Country"), Dimension.Of("Status"), Dimension.Of("IsActive")] }),
        ["period on a value facet"] = ("only a date can be grouped by Month", new() { GroupBy = [Dimension.Of("Country", DateGranularity.Month)] }),
        ["another period on a date facet"] = ("grouping it by Quarter is not supported yet", new() { GroupBy = [Dimension.Of("OrderDate", DateGranularity.Quarter)] }),
        ["text facet as dimension"] = ("Text facet; it has no values", new() { GroupBy = [Dimension.Of("search")] }),
        ["duplicate keys"] = ("Two parts of the query have the key 'orders'", new() { Values = [QueryValue.Metric("orders"), QueryValue.Count().As("orders")] }),
        ["order by unknown key"] = ("orders by 'revenue', which is not one of its columns", new() { GroupBy = [Dimension.Of("Country")], OrderBy = QueryOrder.Descending("revenue") }),
        ["sum of text"] = ("Sum needs numbers, and facet 'Country'", new() { Values = [QueryValue.Sum("Country")] }),
        ["distinct of a text facet"] = ("Distinct needs values; facet 'search'", new() { Values = [QueryValue.Distinct("search")] }),
        ["misspelt metric"] = ("Did you mean 'revenue'?", new() { Values = [QueryValue.Metric("revnue")] }),
        ["negative take"] = ("Take must be zero or more", new() { Take = -1 }),
        ["unknown selection key"] = ("Unknown facet key 'Region'", new() { Selections = Selections.Empty.With("Region", ValueSelection.Of("North")) }),
    };

    /// <summary>A facet's non-zero filtered counts keyed the way <see cref="GroupKey"/> keys a query's cells.</summary>
    private static Dictionary<object, int> FilteredCounts(FacetState facet) => facet switch
    {
        ValueFacetState values => values.Values.Where(v => v.FilteredCount > 0).ToDictionary(v => v.Value ?? NullKey, v => v.FilteredCount),
        RangeFacetState range => range.Buckets.Where(b => b.FilteredCount > 0).Select(b => (Key: (object)b.ToInterval().From!.GetValueOrDefault(double.NegativeInfinity), b.FilteredCount))
            .Concat(range.Null.FilteredCount > 0 ? [(NullKey, range.Null.FilteredCount)] : [])
            .ToDictionary(p => p.Key, p => p.FilteredCount),
        DateFacetState date => date.Buckets.Where(b => b.FilteredCount > 0).Select(b => (Key: (object)b.From, b.FilteredCount))
            .Concat(date.Null.FilteredCount > 0 ? [(NullKey, date.Null.FilteredCount)] : [])
            .ToDictionary(p => p.Key, p => p.FilteredCount),
        _ => throw new InvalidOperationException(facet.Kind.ToString()),
    };

    private static readonly object NullKey = new();

    private static object GroupKey(object? cell) => cell switch
    {
        null => NullKey,
        QueryValueCell value => value.Value!,
        RangeInterval interval => interval.From ?? double.NegativeInfinity,
        QueryPeriod period => period.Interval.From!.Value,
        _ => throw new InvalidOperationException(cell.GetType().Name),
    };
}
