using BenchmarkDotNet.Attributes;

namespace Linq2Dashboard.Benchmarks;

/// <summary>
/// Design §10.3's estimates measured (§10.8 step 6): queries over a million rows, all of them matching unless a
/// scenario selects. Selection row sets are warm, as after a user's clicks; the state cache is never filled, so no
/// query takes its matching set for free.
/// </summary>
[MemoryDiagnoser]
public class QueryBenchmarks
{
    private const int Rows = 1_000_000;

    private static readonly DashboardQuery<BenchmarkOrder> ByFacets = new()
    {
        GroupBy = [Dimension.Of("Country"), Dimension.Of("Status")],
        Values = [QueryValue.Metric("revenue"), QueryValue.Metric("orders")],
    };

    private Dashboard<BenchmarkOrder> dashboard = null!;
    private Dashboard<BenchmarkOrder> parallel = null!;
    private string json = null!;

    [GlobalSetup]
    public void Setup()
    {
        BenchmarkOrder[] orders = OrderGenerator.Generate(Rows);
        dashboard = DashboardFactory.Build(orders);
        parallel = DashboardFactory.Build(orders, parallelCounting: true);
        dashboard.Query(SelectedByFacets());
        json = """{ "selections": "Status=Open,Pending", "groupBy": ["Country", "OrderDate:Quarter"], "values": ["revenue", "count", "avg:Amount"], "orderBy": "-revenue", "take": 10 }""";
    }

    private static DashboardQuery<BenchmarkOrder> SelectedByFacets() => ByFacets with { Selections = DashboardFactory.Scenarios.Three };

    /// <summary>Two facets, two metrics, every row: the code columns and the metric column, one pass. Estimate 5 to 10 ms.</summary>
    [Benchmark(Baseline = true)]
    public QueryResult GroupBy_TwoFacets() => dashboard.Query(ByFacets);

    [Benchmark]
    public QueryResult GroupBy_TwoFacets_ThreeSelections() => dashboard.Query(SelectedByFacets());

    /// <summary>The same with a where over every row. Estimate 2 to 10 ms more.</summary>
    [Benchmark]
    public QueryResult GroupBy_TwoFacets_Where() => dashboard.Query(ByFacets with { Where = x => x.Quantity > 5 });

    [Benchmark]
    public QueryResult GroupBy_TwoFacets_Where_Parallel() => parallel.Query(ByFacets with { Where = x => x.Quantity > 5 });

    /// <summary>A plain property with 20 values: a getter and a hash per row. Estimate 20 to 40 ms.</summary>
    [Benchmark]
    public QueryResult GroupBy_Property() => dashboard.Query(new() { GroupBy = [Dimension.Of("Quantity")], Values = [QueryValue.Metric("revenue")] });

    /// <summary>A plain string property with 100 000 values, compared ignoring case.</summary>
    [Benchmark]
    public QueryResult GroupBy_Property_100000Values() => dashboard.Query(new() { GroupBy = [Dimension.Property("Customer")], Take = 10, OrderBy = QueryOrder.Descending("Count") });

    /// <summary>The customer facet's 100 000 values through its code column, for comparison.</summary>
    [Benchmark]
    public QueryResult GroupBy_Facet_100000Values() => dashboard.Query(new() { GroupBy = [Dimension.Of("Customer")], Take = 10, OrderBy = QueryOrder.Descending("Count") });

    /// <summary>Distinct customers per country: one hash of (group, code) pairs. Estimate about 30 ms more.</summary>
    [Benchmark]
    public QueryResult Distinct_PerGroup() => dashboard.Query(new() { GroupBy = [Dimension.Of("Country")], Values = [QueryValue.Distinct("Customer")] });

    /// <summary>The monthly date facet by quarter: its buckets map once (design §10.3).</summary>
    [Benchmark]
    public QueryResult Period_Nested() => dashboard.Query(new() { GroupBy = [Dimension.Of("OrderDate", DateGranularity.Quarter)] });

    /// <summary>The monthly date facet by week: each row's instant converted in the facet's zone.</summary>
    [Benchmark]
    public QueryResult Period_PerRow() => dashboard.Query(new() { GroupBy = [Dimension.Of("OrderDate", DateGranularity.Week)] });

    /// <summary>The plain date property by month, in UTC.</summary>
    [Benchmark]
    public QueryResult Period_Property() => dashboard.Query(new() { GroupBy = [Dimension.Property("OrderDate", DateGranularity.Month)] });

    /// <summary>The ten largest orders by a facet's number: one key per row into a heap of ten. Estimate 5 to 20 ms.</summary>
    [Benchmark]
    public QueryResult Rows_Top10_Facet() => dashboard.Query(new()
    {
        Columns = [Dimension.Of("Id"), Dimension.Of("Customer"), Dimension.Of("Amount")],
        OrderBy = QueryOrder.Descending("Amount"),
        Take = 10,
    });

    /// <summary>The same ordered by a plain property.</summary>
    [Benchmark]
    public QueryResult Rows_Top10_Property() => dashboard.Query(new()
    {
        Columns = [Dimension.Of("Id"), Dimension.Of("Quantity")],
        OrderBy = QueryOrder.Descending("Quantity"),
        Take = 10,
    });

    /// <summary>The first hundred rows in the dashboard's own order.</summary>
    [Benchmark]
    public QueryResult Rows_First100() => dashboard.Query(new() { Columns = [Dimension.Of("Id"), Dimension.Of("Customer")] });

    /// <summary>An agent's round trip: read the query from JSON, answer it, write the answer.</summary>
    [Benchmark]
    public string Json_RoundTrip() => dashboard.Serializer.ToJson(dashboard.Query(dashboard.QueryFromJson(json)));

    [Benchmark]
    public string Describe_Json() => dashboard.Serializer.ToJson(dashboard.Describe());
}
