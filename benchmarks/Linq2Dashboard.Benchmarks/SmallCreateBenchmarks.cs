using BenchmarkDotNet.Attributes;

namespace Linq2Dashboard.Benchmarks;

/// <summary>
/// Design §8: the dashboard built per page visit over a small dataset, with no cache. Zero rows leaves
/// the fixed cost of a build, mostly compiling the selectors; the rest shows where the row work takes over.
/// </summary>
[MemoryDiagnoser]
public class SmallCreateBenchmarks
{
    private BenchmarkOrder[] orders = [];
    private Dashboard<BenchmarkOrder> dashboard = null!;

    [Params(0, 1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        orders = OrderGenerator.Generate(Rows);
        dashboard = DashboardFactory.Build(orders);
    }

    /// <summary>The full design §8 definition: eight facets, a text facet, three metrics and a sort order.</summary>
    [Benchmark(Baseline = true)]
    public Dashboard<BenchmarkOrder> Create() => DashboardFactory.Build(orders);

    /// <summary>What a page visit pays before it can render: the build and the first state.</summary>
    [Benchmark]
    public DashboardState<BenchmarkOrder> CreateAndCalculate() => DashboardFactory.Build(orders).Calculate();

    /// <summary>A click on a dashboard of this size: three facets, both caches cold.</summary>
    [Benchmark]
    public DashboardState<BenchmarkOrder> Calculate_ThreeFacets_Cold()
    {
        dashboard.ClearCaches();
        return dashboard.CalculateUncached(DashboardFactory.Scenarios.Three);
    }
}
