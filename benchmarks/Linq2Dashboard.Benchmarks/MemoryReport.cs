using System.Diagnostics;

namespace Linq2Dashboard.Benchmarks;

/// <summary>
/// Design §8: managed memory per facet kind at a million rows, plus build and first-calculation
/// wall times. Not a BenchmarkDotNet benchmark: it builds one dashboard per facet and measures the
/// heap before and after, which BenchmarkDotNet's allocation counter cannot attribute per facet.
/// </summary>
public static class MemoryReport
{
    public static void Run(int rows = 1_000_000)
    {
        Console.WriteLine($"Memory report, {rows:N0} rows");
        Console.WriteLine();

        var watch = Stopwatch.StartNew();
        BenchmarkOrder[] orders = OrderGenerator.Generate(rows);
        Console.WriteLine($"{"generate dataset",-32} {watch.ElapsedMilliseconds,8:N0} ms");

        // A dashboard with nothing configured still copies the row array; everything else is reported above that.
        long baseline = Retained(() => Dashboard.Create(orders, _ => { }));
        Console.WriteLine($"{"empty dashboard (row array)",-32} {"",8}      {baseline / 1_048_576.0,8:N1} MB");
        Console.WriteLine();

        Console.WriteLine($"{"facet",-32} {"build ms",8}   {"MB above baseline",17}");
        Report("Country (20 values)", orders, baseline, b => b.ValueFacet(x => x.Country));
        Report("Status (5 values)", orders, baseline, b => b.ValueFacet(x => x.Status));
        Report("Category (200 values)", orders, baseline, b => b.ValueFacet(x => x.Category).Top(20));
        Report("Brand (2 000 values)", orders, baseline, b => b.ValueFacet(x => x.Brand).Top(20));
        Report("Customer (100 000 values)", orders, baseline, b => b.ValueFacet(x => x.Customer).Top(20).Searchable());
        Report("IsActive (boolean)", orders, baseline, b => b.BooleanFacet(x => x.IsActive));
        Report("Amount (range, 8 buckets)", orders, baseline, b => b.RangeFacet(x => x.Amount).Buckets(50, 100, 200, 500, 1000, 2000, 5000));
        Report("OrderDate (date, month)", orders, baseline, b => b.DateFacet(x => x.OrderDate).TimeZone(DashboardFactory.Stockholm));
        Report("Sum metric", orders, baseline, b => b.SumMetric("revenue", x => x.Amount));
        Report("OrderBy (sort order)", orders, baseline, b => b.OrderByDescending(x => x.OrderDate));
        Console.WriteLine();

        watch.Restart();
        Dashboard<BenchmarkOrder> full = DashboardFactory.Build(orders);
        long buildMs = watch.ElapsedMilliseconds;
        long fullBytes = Retained(() => DashboardFactory.Build(orders));
        watch.Restart();
        DashboardState<BenchmarkOrder> state = full.CalculateUncached(DashboardFactory.Scenarios.Three);
        long firstCalcMs = watch.ElapsedMilliseconds;
        watch.Restart();
        full.CalculateUncached(DashboardFactory.Scenarios.Three);
        long warmCalcMs = watch.ElapsedMilliseconds;

        Console.WriteLine($"{"full dashboard build",-32} {buildMs,8:N0} ms   {fullBytes / 1_048_576.0,8:N1} MB including row array");
        Console.WriteLine($"{"first Calculate (3 facets)",-32} {firstCalcMs,8:N0} ms   matching {state.MatchingCount:N0}");
        Console.WriteLine($"{"warm Calculate (3 facets)",-32} {warmCalcMs,8:N0} ms");
    }

    private static void Report(string name, BenchmarkOrder[] orders, long baseline, Action<DashboardBuilder<BenchmarkOrder>> configure)
    {
        // Time a build on its own; then build again inside the heap measurement, whose collections would distort the timing.
        var watch = Stopwatch.StartNew();
        GC.KeepAlive(Dashboard.Create(orders, configure));
        long buildMs = watch.ElapsedMilliseconds;

        long bytes = Retained(() => Dashboard.Create(orders, configure));
        Console.WriteLine($"{name,-32} {buildMs,8:N0}   {(bytes - baseline) / 1_048_576.0,17:N1}");
    }

    /// <summary>Heap growth attributable to <paramref name="build"/>'s result, after full collections.</summary>
    private static long Retained<T>(Func<T> build) where T : class
    {
        long before = Settle();
        T keep = build();
        long after = Settle();
        GC.KeepAlive(keep);
        return Math.Max(0, after - before);
    }

    private static long Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
