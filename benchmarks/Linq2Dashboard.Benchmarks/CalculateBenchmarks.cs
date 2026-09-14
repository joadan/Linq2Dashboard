using BenchmarkDotNet.Attributes;

namespace Linq2Dashboard.Benchmarks;

/// <summary>
/// Design §8 scenarios 2 to 7: calculating, searching and paging at a million rows.
/// Target: a warm calculation under 50 ms in every scenario.
/// </summary>
/// <remarks>
/// "Warm" means the row-set cache holds every selection's row set and only the contexts, counting,
/// metrics and presentation run: the steady state of a user clicking around. "Cold" empties both
/// caches first, so the selection scans run too. The state cache is bypassed throughout; with it
/// a repeated calculation is a dictionary lookup and would hide the work.
/// </remarks>
[MemoryDiagnoser]
public class CalculateBenchmarks
{
    private Dashboard<BenchmarkOrder> dashboard = null!;
    private DashboardState<BenchmarkOrder> noSelection = null!;
    private DashboardState<BenchmarkOrder> threeSelections = null!;

    [Params(1_000_000)]
    public int Rows { get; set; }

    [Params(false, true)]
    public bool Parallel { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        dashboard = DashboardFactory.Build(OrderGenerator.Generate(Rows), Parallel);

        // Warm the row-set cache for every scenario and keep two states for search and paging.
        noSelection = dashboard.CalculateUncached(Selections.Empty);
        dashboard.CalculateUncached(DashboardFactory.Scenarios.One);
        threeSelections = dashboard.CalculateUncached(DashboardFactory.Scenarios.Three);
        dashboard.CalculateUncached(DashboardFactory.Scenarios.Five);
        dashboard.CalculateUncached(DashboardFactory.Scenarios.RangeAndDate);
        dashboard.CalculateUncached(DashboardFactory.Scenarios.ThreePlusClick);
        ((ValueFacetState)noSelection.Facet("Customer")).Search("Customer 0042");
    }

    [Benchmark(Baseline = true)]
    public DashboardState<BenchmarkOrder> NoSelection() =>
        dashboard.CalculateUncached(Selections.Empty);

    [Benchmark]
    public DashboardState<BenchmarkOrder> OneFacet_Warm() =>
        dashboard.CalculateUncached(DashboardFactory.Scenarios.One);

    [Benchmark]
    public DashboardState<BenchmarkOrder> ThreeFacets_Warm() =>
        dashboard.CalculateUncached(DashboardFactory.Scenarios.Three);

    [Benchmark]
    public DashboardState<BenchmarkOrder> FiveFacets_Warm() =>
        dashboard.CalculateUncached(DashboardFactory.Scenarios.Five);

    [Benchmark]
    public DashboardState<BenchmarkOrder> RangeAndDate_Warm() =>
        dashboard.CalculateUncached(DashboardFactory.Scenarios.RangeAndDate);

    /// <summary>The typical click: three facets warm, one new value toggled into Country so only that scan is cold.</summary>
    [Benchmark]
    public DashboardState<BenchmarkOrder> Click_OneNewValue()
    {
        dashboard.ClearCaches();
        dashboard.CalculateUncached(DashboardFactory.Scenarios.Three);
        return dashboard.CalculateUncached(DashboardFactory.Scenarios.ThreePlusClick);
    }

    [Benchmark]
    public DashboardState<BenchmarkOrder> ThreeFacets_Cold()
    {
        dashboard.ClearCaches();
        return dashboard.CalculateUncached(DashboardFactory.Scenarios.Three);
    }

    [Benchmark]
    public DashboardState<BenchmarkOrder> RangeAndDate_Cold()
    {
        dashboard.ClearCaches();
        return dashboard.CalculateUncached(DashboardFactory.Scenarios.RangeAndDate);
    }

    /// <summary>A new text in the text facet: the predicate scan over every row, serial or parallel with the option (design §4.1).</summary>
    [Benchmark]
    public DashboardState<BenchmarkOrder> Text_Cold()
    {
        dashboard.ClearCaches();
        return dashboard.CalculateUncached(DashboardFactory.Scenarios.Text);
    }

    [Benchmark]
    public IReadOnlyList<FacetValue> Search_Customer() =>
        ((ValueFacetState)threeSelections.Facet("Customer")).Search("Customer 0042");

    [Benchmark]
    public ResultPage<BenchmarkOrder> Page_First() => threeSelections.GetPage(0, 50);

    [Benchmark]
    public ResultPage<BenchmarkOrder> Page_Middle() => threeSelections.GetPage(threeSelections.PageCount(50) / 2, 50);

    [Benchmark]
    public ResultPage<BenchmarkOrder> Page_Last() => threeSelections.GetPage(threeSelections.PageCount(50) - 1, 50);
}
