namespace Linq2Dashboard.Benchmarks;

/// <summary>The dashboard configuration from design §8: eight facets, three metrics, a sort order.</summary>
public static class DashboardFactory
{
    public static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public static Dashboard<BenchmarkOrder> Build(BenchmarkOrder[] orders, bool parallelCounting = false) =>
        Dashboard.Create(orders, b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.ValueFacet(x => x.Category).Top(20);
            b.ValueFacet(x => x.Brand).Top(20);
            b.ValueFacet(x => x.Customer).Top(20).Searchable();
            b.BooleanFacet(x => x.IsActive);
            b.RangeFacet(x => x.Amount).Buckets(50, 100, 200, 500, 1000, 2000, 5000);
            b.DateFacet(x => x.OrderDate)
             .TimeZone(Stockholm)
             .Granularity(DateGranularity.Month)
             .Presets(DatePreset.Last30Days, DatePreset.ThisYear);
            b.TextFacet("search", (x, text) =>
                x.Customer.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Brand.Contains(text, StringComparison.OrdinalIgnoreCase));
            b.CountMetric("orders");
            b.SumMetric("revenue", x => x.Amount);
            b.AverageMetric("average", x => x.Amount);
            b.OrderByDescending(x => x.OrderDate);
            b.EnableParallelCounting(parallelCounting);
        });

    /// <summary>Selections used by the calculate scenarios, from one facet up to five.</summary>
    public static class Scenarios
    {
        public static readonly Selections One = Selections.Empty
            .With("Country", ValueSelection.Of("C00"));

        public static readonly Selections Three = One
            .With("Status", ValueSelection.Of("Open", "Pending"))
            .With("IsActive", ValueSelection.Of(true));

        public static readonly Selections Five = Three
            .With("Category", ValueSelection.Of("Category 000", "Category 001"))
            .With("Brand", ValueSelection.Of("Brand 0000"));

        public static readonly Selections RangeAndDate = Selections.Empty
            .With("Amount", RangeSelection.Between(100, 1000))
            .With("OrderDate", DateSelection.Between(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)),
                new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.FromHours(2))));

        /// <summary>A click: the previous selections with one more Country value toggled in.</summary>
        public static readonly Selections ThreePlusClick = Three.Toggle("Country", "C01");

        /// <summary>A text typed into the text facet: two case-insensitive Contains per row (concept §5).</summary>
        public static readonly Selections Text = Selections.Empty
            .With("search", new TextSelection("Customer 0042"));
    }
}
