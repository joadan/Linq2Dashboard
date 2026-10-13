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
    public void Query_where_narrows_the_rows_the_selections_match()
    {
        var answer = Ask(new()
        {
            Selections = Selections.Empty.With("IsActive", ValueSelection.Of(true)),
            Where = x => x.Amount >= 500,
            GroupBy = [Dimension.Of("Country")],
        });

        Assert.Equal([("SE", 2.0)], answer.Rows.Select(r => (ValueOf(r[0]), (double)r["Count"]!)));
    }

    [Fact]
    public void Query_where_gives_the_same_answer_on_several_cores()
    {
        Order[] many = Enumerable.Range(0, 50).SelectMany(_ => TestData.Orders()).ToArray();
        var serial = Dashboard.Create(many, b => b.ValueFacet(x => x.Country));
        var query = new DashboardQuery<Order> { Where = x => x.Amount >= 500 && x.Status != "Pending", GroupBy = [Dimension.Of("Country")] };

        var expected = serial.Query(query).Rows.Select(r => r.Cells.ToArray());
        var actual = Dashboard.Create(many, b => { b.ValueFacet(x => x.Country); b.EnableParallelCounting(); }).Query(query).Rows.Select(r => r.Cells.ToArray());

        Assert.Equal(expected, actual);
        Assert.Equal([("SE", 100.0), (null, 50.0)], serial.Query(query).Rows.Select(r => (ValueOf(r[0]), (double)r["Count"]!)));
    }

    [Fact]
    public void Query_groups_by_a_plain_property_with_a_value_facets_rules()
    {
        var kind = Ask(new() { GroupBy = [Dimension.Of("Kind")] });
        Assert.Equal([(OrderKind.Web, 5.0), (OrderKind.Store, 3.0)], kind.Rows.Select(r => ((OrderKind)ValueOf(r[0])!, (double)r["Count"]!)));
        Assert.Equal(new QueryColumn("Kind", "Kind", QueryColumnRole.Dimension, QueryColumnKind.Value) { Source = "Kind" }, kind.Column("Kind"));

        // Strings ignore case and keep the first spelling, null is a group, as under the Country facet.
        var country = Ask(new() { GroupBy = [Dimension.Property("Country")] });
        Assert.Equal([("DK", 1.0), ("NO", 2.0), ("SE", 3.0), (null, 2.0)], country.Rows.Select(r => (ValueOf(r[0]), (double)r["Count"]!)));
    }

    [Fact]
    public void Query_name_means_the_facet_before_the_property()
    {
        var facet = Ask(new() { GroupBy = [Dimension.Of("Amount")] });
        var property = Ask(new() { GroupBy = [Dimension.Property("Amount")] });

        Assert.Equal(QueryColumnKind.Interval, facet.Column("Amount").Kind);
        Assert.Equal(4, facet.Total);
        Assert.Equal(QueryColumnKind.Value, property.Column("Amount").Kind);
        Assert.Equal([0m, 75m, 100m, 250m, 500m, 999.5m, 1000m, 2500m], property.Rows.Select(r => (decimal)ValueOf(r[0])!));
    }

    [Fact]
    public void Query_groups_a_collection_property_by_item()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Property("Tags")] });

        Assert.Equal(
            [("b2b", 3.0), ("gift", 3.0), ("urgent", 2.0), (null, 3.0)],
            answer.Rows.Select(r => (ValueOf(r[0]), (double)r["Count"]!)));
    }

    [Fact]
    public void Query_groups_a_plain_date_property_by_period_in_utc()
    {
        var shipped = Ask(new() { GroupBy = [Dimension.Of("Shipped", DateGranularity.Month)] });

        Assert.Equal("Month(Shipped)", shipped.Columns[0].Key);
        Assert.Equal(
            [(new DateTime(2026, 1, 1), 1.0), (new DateTime(2026, 2, 1), 1.0), (new DateTime(2026, 3, 1), 1.0), (new DateTime(2026, 4, 1), 2.0)],
            shipped.Rows.Take(4).Select(r => (((QueryPeriod)r[0]!).PeriodStart, (double)r["Count"]!)));
        Assert.Null(shipped.Rows[^1][0]);
        Assert.Equal(TestData.Instant("2026-01-01T00:00:00Z"), ((QueryPeriod)shipped.Rows[0][0]!).Interval.From);

        var due = Ask(new() { GroupBy = [Dimension.Of("Due", DateGranularity.Quarter)] });
        Assert.Equal([5.0, 3.0], due.Rows.Select(r => (double)r["Count"]!));
    }

    [Theory]
    [InlineData(DateGranularity.Quarter)]
    [InlineData(DateGranularity.Year)]
    [InlineData(DateGranularity.Week)]
    [InlineData(DateGranularity.Day)]
    public void Query_groups_a_date_facet_by_another_period_in_its_zone(DateGranularity period)
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("OrderDate", period)] });

        var periods = answer.Rows.Select(r => (QueryPeriod)r[0]!).ToArray();
        Assert.All(periods, p => Assert.Equal(period, p.Granularity));
        Assert.Equal(periods.OrderBy(p => p.PeriodStart), periods);
        Assert.Equal(8.0, answer.Rows.Sum(r => (double)r["Count"]!));
        foreach (QueryRow row in answer.Rows)
        {
            var selections = Selections.Empty.ToggleInterval("OrderDate", ((QueryPeriod)row[0]!).Interval);
            Assert.Equal((double)row["Count"]!, Shared.Calculate(selections).MatchingCount);
        }
    }

    [Fact]
    public void Query_groups_a_monthly_date_facet_by_quarter_in_its_zone()
    {
        var answer = Ask(new() { GroupBy = [Dimension.Of("OrderDate", DateGranularity.Quarter)] });

        // 31 March 22:30 in Stockholm is still the first quarter there.
        Assert.Equal(
            [(new DateTime(2026, 1, 1), 6.0), (new DateTime(2026, 4, 1), 2.0)],
            answer.Rows.Select(r => (((QueryPeriod)r[0]!).PeriodStart, (double)r["Count"]!)));
        Assert.Equal(TestData.Instant("2026-04-01T00:00:00+02:00"), ((QueryPeriod)answer.Rows[1][0]!).Interval.From);
    }

    [Fact]
    public void Query_aggregates_plain_properties()
    {
        var answer = Ask(new()
        {
            GroupBy = [Dimension.Of("Status")],
            Values = [QueryValue.Sum("Discount"), QueryValue.Distinct("Kind"), QueryValue.Distinct("Tags")],
        });

        Assert.Equal(
            [("Closed", (double?)50.0, 1.0, 2.0), ("Open", 265.0, 2.0, 3.0), ("Pending", null, 1.0, null)],
            answer.Rows.Select(r => (ValueOf(r[0]), (double?)r[1], (double)r[2]!, (double?)r[3])));
    }

    [Fact]
    public void Row_query_lists_what_each_column_reads_from_the_row()
    {
        var answer = Ask(new()
        {
            Columns = [Dimension.Of("Id"), Dimension.Of("Country"), Dimension.Of("Amount"), Dimension.Of("OrderDate"), Dimension.Of("Tags"), Dimension.Of("Shipped")],
            Take = 2,
        });

        Assert.Equal(
            [QueryColumnKind.Value, QueryColumnKind.Value, QueryColumnKind.Number, QueryColumnKind.Instant, QueryColumnKind.Value, QueryColumnKind.Instant],
            answer.Columns.Select(c => c.Kind));
        Assert.All(answer.Columns, c => Assert.Equal(QueryColumnRole.Dimension, c.Role));

        QueryRow first = answer.Rows[0];
        Assert.Equal(new QueryValueCell(1), first["Id"]);
        Assert.Equal(new QueryValueCell("SE"), first["Country"]);
        Assert.Equal(100.0, first["Amount"]);
        Assert.Equal(TestData.Instant("2026-01-15T10:00:00+01:00"), first["OrderDate"]);
        Assert.Equal(TimeSpan.FromHours(1), ((DateTimeOffset)first["OrderDate"]!).Offset);
        Assert.Equal([new QueryValueCell("urgent"), new QueryValueCell("gift")], (IReadOnlyList<QueryValueCell>)first["Tags"]!);
        Assert.Equal(TestData.Instant("2026-01-16T08:00:00Z"), first["Shipped"]);
        Assert.Null(answer.Rows[1]["Shipped"]);
        Assert.Equal(8, answer.Total);
        Assert.True(answer.IsCut);
    }

    [Fact]
    public void Row_query_returns_the_first_100_rows_in_the_dashboards_own_order_without_a_take()
    {
        var answer = Ask(new() { Columns = [Dimension.Of("Id")] });
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], answer.Rows.Select(r => (int)ValueOf(r[0])!));
        Assert.False(answer.IsCut);

        var byAmount = Dashboard.Create(TestData.Orders(), b => b.OrderByDescending(x => x.Amount));
        var ordered = byAmount.Query(new() { Columns = [Dimension.Of("Id")], Take = 3 });
        Assert.Equal([6, 5, 4], ordered.Rows.Select(r => (int)ValueOf(r[0])!));
    }

    [Fact]
    public void Row_query_orders_by_a_column_with_null_last_in_both_directions()
    {
        QueryResult Ordered(QueryOrder order, int take) =>
            Ask(new() { Columns = [Dimension.Of("Id"), Dimension.Of("Discount")], OrderBy = order, Take = take });

        Assert.Equal([4, 8, 1, 3, 6, 2, 5, 7], Ordered(QueryOrder.Ascending("Discount"), 8).Rows.Select(r => (int)ValueOf(r[0])!));
        Assert.Equal([6, 3, 1, 8, 4, 2, 5, 7], Ordered(QueryOrder.Descending("Discount"), 8).Rows.Select(r => (int)ValueOf(r[0])!));
        Assert.Equal([6, 3], Ordered(QueryOrder.Descending("Discount"), 2).Rows.Select(r => (int)ValueOf(r[0])!));

        var largest = Ask(new() { Columns = [Dimension.Of("Id"), Dimension.Of("Amount")], OrderBy = QueryOrder.Descending("Amount"), Take = 3 });
        Assert.Equal([(6, 2500.0), (5, 1000.0), (4, 999.5)], largest.Rows.Select(r => ((int)ValueOf(r[0])!, (double)r[1]!)));
    }

    [Fact]
    public void Row_query_applies_the_selections_and_the_where()
    {
        var answer = Ask(new()
        {
            Selections = Selections.Empty.With("Status", ValueSelection.Of("Open")),
            Where = x => x.Amount < 1000,
            Columns = [Dimension.Of("Id")],
        });

        Assert.Equal([1, 2, 8], answer.Rows.Select(r => (int)ValueOf(r[0])!));
        Assert.Equal(3, answer.Total);
    }

    [Fact]
    public void Row_query_columns_can_name_a_period_and_a_collection_property()
    {
        var answer = Ask(new()
        {
            Columns = [Dimension.Of("Id"), Dimension.Of("OrderDate", DateGranularity.Month), Dimension.Property("Tags")],
            OrderBy = QueryOrder.Ascending("Id"),
        });

        Assert.Equal(new DateTime(2026, 3, 1), ((QueryPeriod)answer.Rows[3]["Month(OrderDate)"]!).PeriodStart);
        // The property's own items, repeats in another case dropped; no items, or only a null, is null.
        Assert.Equal([new QueryValueCell("Urgent"), new QueryValueCell("b2b")], (IReadOnlyList<QueryValueCell>)answer.Rows[3]["Tags"]!);
        Assert.Null(answer.Rows[4]["Tags"]);
        Assert.Null(answer.Rows[6]["Tags"]);
    }

    [Fact]
    public void Row_query_throws_when_cancelled()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => Shared.Query(new() { Columns = [Dimension.Of("Id")] }, cancelled.Token));
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
        ["unknown dimension"] = ("'Customer' is neither a facet of this dashboard nor a property of Order. Facets: Country, Status", new() { GroupBy = [Dimension.Of("Customer")] }),
        ["unknown property"] = ("'Customer' is not a property of Order. Properties: Id, Country", new() { GroupBy = [Dimension.Property("Customer")] }),
        ["property in the wrong case"] = ("Did you mean 'Kind'?", new() { GroupBy = [Dimension.Of("kind")] }),
        ["period on a property that is not a date"] = ("Property 'Kind' (OrderKind) is not a date", new() { GroupBy = [Dimension.Of("Kind", DateGranularity.Month)] }),
        ["period on a collection property"] = ("Property 'Tags' (IReadOnlyList<String?>?) is a collection", new() { GroupBy = [Dimension.Property("Tags", DateGranularity.Month)] }),
        ["sum of a property that is not a number"] = ("Sum needs numbers, and property 'Kind' (OrderKind) does not hold numbers", new() { Values = [QueryValue.Sum("Kind")] }),
        ["metric as dimension"] = ("'revenue' is a metric", new() { GroupBy = [Dimension.Of("revenue")] }),
        ["three dimensions"] = ("at most two dimensions", new() { GroupBy = [Dimension.Of("Country"), Dimension.Of("Status"), Dimension.Of("IsActive")] }),
        ["period on a value facet"] = ("only a date can be grouped by Month", new() { GroupBy = [Dimension.Of("Country", DateGranularity.Month)] }),
        ["text facet as dimension"] = ("Text facet; it has no values", new() { GroupBy = [Dimension.Of("search")] }),
        ["duplicate keys"] = ("Two parts of the query have the key 'orders'", new() { Values = [QueryValue.Metric("orders"), QueryValue.Count().As("orders")] }),
        ["order by unknown key"] = ("orders by 'revenue', which is not one of its columns", new() { GroupBy = [Dimension.Of("Country")], OrderBy = QueryOrder.Descending("revenue") }),
        ["sum of text"] = ("Sum needs numbers, and facet 'Country'", new() { Values = [QueryValue.Sum("Country")] }),
        ["distinct of a text facet"] = ("Distinct needs values; facet 'search'", new() { Values = [QueryValue.Distinct("search")] }),
        ["misspelt metric"] = ("Did you mean 'revenue'?", new() { Values = [QueryValue.Metric("revnue")] }),
        ["negative take"] = ("Take must be zero or more", new() { Take = -1 }),
        ["unknown selection key"] = ("Unknown facet key 'Region'", new() { Selections = Selections.Empty.With("Region", ValueSelection.Of("North")) }),
        ["row query with groups"] = ("Columns instead of GroupBy and Values", new() { Columns = [Dimension.Of("Id")], GroupBy = [Dimension.Of("Country")] }),
        ["row query above the ceiling"] = ("A row query returns at most 10000 rows; this one asks for 10001", new() { Columns = [Dimension.Of("Id")], Take = 10_001 }),
        ["object property as a column"] = ("Property 'Address' (Address?) holds an object", new() { Columns = [Dimension.Of("Address")] }),
        ["text facet as a column"] = ("it reads nothing from a row", new() { Columns = [Dimension.Of("search")] }),
        ["metric as a column"] = ("'revenue' is a metric, and a query cannot list a metric", new() { Columns = [Dimension.Of("revenue")] }),
        ["order by a list column"] = ("orders by 'Tags', which holds several values per row", new() { Columns = [Dimension.Of("Tags")], OrderBy = QueryOrder.Ascending("Tags") }),
        ["duplicate columns"] = ("Two columns of the query have the key 'Id'", new() { Columns = [Dimension.Of("Id"), Dimension.Property("Id")] }),
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
