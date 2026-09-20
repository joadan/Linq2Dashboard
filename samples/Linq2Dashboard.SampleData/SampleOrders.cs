namespace Linq2Dashboard.SampleData;

public sealed class SampleOrder
{
    public int Id { get; init; }

    public string Country { get; init; } = "";

    public string Status { get; init; } = "";

    public string Category { get; init; } = "";

    public int? CustomerId { get; init; }

    /// <summary>The customer's name, denormalised onto the order; the facet counts by <see cref="CustomerId"/> and labels with this.</summary>
    public string? Customer { get; init; }

    public bool? IsActive { get; init; }

    public decimal? Amount { get; init; }

    public DateTime? OrderDate { get; init; }

    public int Quantity { get; init; }
}

/// <summary>
/// Deterministic sample data: a smaller cousin of the benchmark generator, with nulls sprinkled in.
/// Order dates cover the two years up to today, so the relative presets ("Last 30 days", "This year") always have rows.
/// </summary>
public static class SampleOrders
{
    private static readonly string[] Countries = ["Sweden", "Norway", "Denmark", "Finland", "Germany", "Netherlands", "Poland", "France", "Spain", "Italy", "United Kingdom", "Ireland"];
    private static readonly string[] Statuses = ["Open", "Pending", "Shipped", "Closed", "Cancelled"];
    private static readonly string[] Categories = Enumerable.Range(1, 40).Select(i => $"Category {i:00}").ToArray();
    private static readonly string[] Customers = Enumerable.Range(1, 5_000).Select(i => $"Customer {i:0000}").ToArray();
    private const int RangeMinutes = 2 * 365 * 24 * 60;

    public static SampleOrder[] Generate(int count, int seed = 42)
    {
        var random = new Random(seed);
        DateTime rangeStart = DateTime.UtcNow.Date.AddDays(1).AddMinutes(-RangeMinutes);
        var orders = new SampleOrder[count];
        for (int i = 0; i < count; i++)
        {
            int? customer = random.Next(100) < 3 ? null : Skewed(random, Customers.Length);
            orders[i] = new SampleOrder
            {
                Id = i + 1,
                Country = Countries[Skewed(random, Countries.Length)],
                Status = Statuses[Skewed(random, Statuses.Length)],
                Category = Categories[Skewed(random, Categories.Length)],
                CustomerId = customer is int c ? c + 1 : null,
                Customer = customer is int n ? Customers[n] : null,
                IsActive = random.Next(100) < 2 ? null : random.Next(100) < 80,
                Amount = random.Next(100) < 5 ? null : Math.Round((decimal)(Math.Exp(Gaussian(random) * 1.2) * 150), 2),
                OrderDate = random.Next(100) < 2 ? null : rangeStart.AddMinutes(random.Next(RangeMinutes)),
                Quantity = 1 + Skewed(random, 20),
            };
        }

        return orders;
    }

    public static Dashboard<SampleOrder> BuildDashboard(int rows) => BuildDashboard(Generate(rows));

    /// <summary>Builds the dashboard over already generated orders, so generation and build can be timed apart.</summary>
    public static Dashboard<SampleOrder> BuildDashboard(SampleOrder[] orders) =>
        Dashboard.Create(orders, b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.ValueFacet(x => x.Category).Top(10);
            b.ValueFacet("Customer", x => x.CustomerId).Label(x => x.Customer).Top(10).Searchable();
            b.BooleanFacet(x => x.IsActive).Name("Active");
            b.RangeFacet(x => x.Amount);   // default: about ten round buckets derived from the data, open at both ends
            b.DateFacet(x => x.OrderDate)
             .Name("Order date")
             .TimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"))
             .Granularity(DateGranularity.Month)
             .Presets(DatePreset.Last30Days, DatePreset.ThisYear);
            b.TextFacet("search", (x, text) =>
                (x.Customer?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                || x.Category.Contains(text, StringComparison.OrdinalIgnoreCase))
             .Name("Search");
            b.CountMetric("orders").Name("Orders");
            b.SumMetric("revenue", x => x.Amount).Name("Revenue");
            b.AverageMetric("average", x => x.Amount).Name("Average order");
            b.DistinctMetric("customers", x => x.Customer).Name("Customers");
            b.CalculatedMetric("perCustomer", m => m["revenue"] / m["customers"]).Name("Revenue per customer");
            b.OrderByDescending(x => x.OrderDate);
        });

    private static int Skewed(Random random, int n)
    {
        double u = random.NextDouble();
        return Math.Min(n - 1, (int)(n * u * u));
    }

    private static double Gaussian(Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
