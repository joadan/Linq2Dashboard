using BenchmarkDotNet.Attributes;

namespace Linq2Dashboard.Benchmarks;

/// <summary>Design §8 scenario 1: building the dashboard from an in-memory list. Target: under 2 s at a million rows.</summary>
[MemoryDiagnoser]
public class CreateBenchmarks
{
    private BenchmarkOrder[] orders = [];
    private Dashboard<BenchmarkOrder> dashboard = null!;

    [Params(1_000_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        orders = OrderGenerator.Generate(Rows);
        dashboard = DashboardFactory.Build(orders);
    }

    [Benchmark]
    public Dashboard<BenchmarkOrder> Create() => DashboardFactory.Build(orders);

    /// <summary>The same dashboard without the sort order, to isolate the cost of sorting a million row ids.</summary>
    [Benchmark]
    public Dashboard<BenchmarkOrder> Create_WithoutSort() =>
        Dashboard.Create(orders, b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.ValueFacet(x => x.Category).Top(20);
            b.ValueFacet(x => x.Brand).Top(20);
            b.ValueFacet(x => x.Customer).Top(20).Searchable();
            b.BooleanFacet(x => x.IsActive);
            b.RangeFacet(x => x.Amount).Buckets(50, 100, 200, 500, 1000, 2000, 5000);
            b.DateFacet(x => x.OrderDate).TimeZone(DashboardFactory.Stockholm);
            b.Count("orders");
            b.Sum("revenue", x => x.Amount);
            b.Average("average", x => x.Amount);
        });

    /// <summary>Only the date facet, to isolate the time zone conversion cost noted in the design.</summary>
    [Benchmark]
    public Dashboard<BenchmarkOrder> Create_DateFacetOnly() =>
        Dashboard.Create(orders, b => b.DateFacet(x => x.OrderDate).TimeZone(DashboardFactory.Stockholm));

    /// <summary>A scoped dashboard over about four fifths of the rows (concept §4.10): one predicate pass plus one count per facet and metric, against a full Create.</summary>
    [Benchmark]
    public Dashboard<BenchmarkOrder> Where() => dashboard.Where(x => x.IsActive == true);

    /// <summary>Only the 100k-value customer facet.</summary>
    [Benchmark]
    public Dashboard<BenchmarkOrder> Create_CustomerFacetOnly() =>
        Dashboard.Create(orders, b => b.ValueFacet(x => x.Customer).Top(20).Searchable());
}
