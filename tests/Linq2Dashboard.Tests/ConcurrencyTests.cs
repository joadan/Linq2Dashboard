namespace Linq2Dashboard.Tests;

/// <summary>
/// The dashboard is documented as immutable and thread-safe, and the usage guide tells hosts to register one
/// instance as a singleton that serves every user. These tests exercise that claim directly (design §5).
/// </summary>
public class ConcurrencyTests
{
    private static Dashboard<Order> Build(Action<DashboardBuilder<Order>>? extra = null) =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.BooleanFacet(x => x.IsActive);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm).Presets(DatePreset.ThisMonth);
            b.TextFacet("search", (x, text) => x.Status.Contains(text, StringComparison.OrdinalIgnoreCase));
            b.CountMetric("orders");
            b.SumMetric("revenue", x => x.Amount);
            b.DistinctMetric("countries", x => x.Country);
            b.OrderByDescending(x => x.Amount);
            b.UseTimeProvider(new FixedTimeProvider(TestData.Instant("2026-03-15T10:00:00Z")));
            extra?.Invoke(b);
        });

    /// <summary>More distinct selections than the state cache holds, so threads both hit and miss the caches.</summary>
    private static Selections[] ManySelections()
    {
        string?[] countries = ["SE", "NO", "DK", null];
        string[] statuses = ["Open", "Closed", "Pending"];
        var list = new List<Selections>();
        foreach (string? country in countries)
        {
            foreach (string status in statuses)
            {
                var s = Selections.Empty.With("Country", ValueSelection.Of(country)).With("Status", ValueSelection.Of(status));
                list.Add(s);
                list.Add(s.With("Amount", RangeSelection.Between(100, 1000)));
                list.Add(s.With("OrderDate", DateSelection.Relative(DatePreset.ThisMonth)).With("search", new TextSelection("pen")));
            }
        }

        return list.ToArray();
    }

    private static string Fingerprint(DashboardState<Order> state)
    {
        var facets = state.Facets.Select(f => f switch
        {
            ValueFacetState v => string.Join(";", v.Values.Select(x => $"{x.Value}:{x.TotalCount}/{x.FilteredCount}/{x.Selected}")),
            RangeFacetState r => string.Join(";", r.Buckets.Select(x => $"{x.From}-{x.To}:{x.TotalCount}/{x.FilteredCount}")),
            DateFacetState d => string.Join(";", d.Buckets.Select(x => $"{x.PeriodStart:O}:{x.TotalCount}/{x.FilteredCount}")),
            _ => f.Kind.ToString(),
        });
        var metrics = state.Metrics.Select(m => $"{m.Key}={m.Value}/{m.Share}");
        var rows = state.Items.Select(o => o.Id);
        return $"{state.MatchingCount}|{string.Join("|", facets)}|{string.Join(",", metrics)}|{string.Join(",", rows)}";
    }

    [Fact]
    public void Concurrent_calculations_on_one_dashboard_give_the_same_states_as_sequential_ones()
    {
        var dashboard = Build();
        Selections[] selections = ManySelections();
        string[] expected = selections.Select(s => Fingerprint(Build().Calculate(s))).ToArray();

        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount) };
        Parallel.For(0, selections.Length * 50, options, i =>
        {
            int index = (i * 7) % selections.Length;   // spread the threads over the selections rather than marching in step
            Assert.Equal(expected[index], Fingerprint(dashboard.Calculate(selections[index])));
        });

        Assert.InRange(dashboard.CachedStateCount, 1, 8);
    }

    [Fact]
    public void Concurrent_calculations_with_parallel_counting_give_the_same_states()
    {
        // Parallel counting adds the library's own threads to the callers'; the result must not depend on either.
        var dashboard = Build(b => b.EnableParallelCounting());
        Selections[] selections = ManySelections();
        string[] expected = selections.Select(s => Fingerprint(Build().Calculate(s))).ToArray();

        Parallel.For(0, selections.Length * 20, i =>
        {
            int index = (i * 11) % selections.Length;
            Assert.Equal(expected[index], Fingerprint(dashboard.Calculate(selections[index])));
        });
    }

    [Fact]
    public void Concurrent_scopes_and_calculations_share_the_parent_without_interfering()
    {
        var parent = Build();
        var open = Selections.Empty.With("Status", ValueSelection.Of("Open"));
        int expectedNordic = Build().ScopeTo(o => o.Country is "SE" or "NO" or "se").Calculate(open).MatchingCount;
        int expectedAll = Build().Calculate(open).MatchingCount;

        Parallel.For(0, 500, i =>
        {
            if (i % 2 == 0)
            {
                Assert.Equal(expectedNordic, parent.ScopeTo(o => o.Country is "SE" or "NO" or "se").Calculate(open).MatchingCount);
            }
            else
            {
                Assert.Equal(expectedAll, parent.Calculate(open).MatchingCount);
            }
        });
    }

    [Fact]
    public void A_state_handed_out_is_unchanged_by_later_calculations()
    {
        // The state is a consistent snapshot (concept §4.7): whatever happens to the dashboard's caches afterwards, a state already returned stays as it was.
        var dashboard = Build();
        var first = dashboard.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        string before = Fingerprint(first);

        foreach (Selections s in ManySelections())
        {
            dashboard.Calculate(s);
        }

        Assert.Equal(before, Fingerprint(first));
    }
}
