namespace Linq2Dashboard.Sample.Data;

public sealed class SampleOrder
{
    public int Id { get; init; }

    public string Country { get; init; } = "";

    public string Status { get; init; } = "";

    public string Category { get; init; } = "";

    public string? Customer { get; init; }

    public bool? IsActive { get; init; }

    public decimal? Amount { get; init; }

    public DateTime? OrderDate { get; init; }

    public int Quantity { get; init; }
}

/// <summary>Deterministic sample data: a smaller cousin of the benchmark generator, with nulls sprinkled in.</summary>
public static class SampleData
{
    private static readonly string[] Countries = ["Sweden", "Norway", "Denmark", "Finland", "Germany", "Netherlands", "Poland", "France", "Spain", "Italy", "United Kingdom", "Ireland"];
    private static readonly string[] Statuses = ["Open", "Pending", "Shipped", "Closed", "Cancelled"];
    private static readonly string[] Categories = Enumerable.Range(1, 40).Select(i => $"Category {i:00}").ToArray();
    private static readonly string[] Customers = Enumerable.Range(1, 5_000).Select(i => $"Customer {i:0000}").ToArray();
    private static readonly DateTime RangeStart = new(2024, 1, 1);
    private const int RangeMinutes = 2 * 365 * 24 * 60;

    public static SampleOrder[] Generate(int count, int seed = 42)
    {
        var random = new Random(seed);
        var orders = new SampleOrder[count];
        for (int i = 0; i < count; i++)
        {
            orders[i] = new SampleOrder
            {
                Id = i + 1,
                Country = Countries[Skewed(random, Countries.Length)],
                Status = Statuses[Skewed(random, Statuses.Length)],
                Category = Categories[Skewed(random, Categories.Length)],
                Customer = random.Next(100) < 3 ? null : Customers[Skewed(random, Customers.Length)],
                IsActive = random.Next(100) < 2 ? null : random.Next(100) < 80,
                Amount = random.Next(100) < 5 ? null : Math.Round((decimal)(Math.Exp(Gaussian(random) * 1.2) * 150), 2),
                OrderDate = random.Next(100) < 2 ? null : RangeStart.AddMinutes(random.Next(RangeMinutes)),
                Quantity = 1 + Skewed(random, 20),
            };
        }

        return orders;
    }

    public static Dashboard<SampleOrder> BuildDashboard(int rows) =>
        Dashboard.Create(Generate(rows), b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.ValueFacet(x => x.Category).Top(10);
            b.ValueFacet(x => x.Customer).Top(10).Searchable();
            b.BooleanFacet(x => x.IsActive).Title("Active");
            b.RangeFacet(x => x.Amount).Buckets(50, 100, 200, 500, 1000, 2000);
            b.DateFacet(x => x.OrderDate)
             .Title("Order date")
             .TimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"))
             .Granularity(DateGranularity.Month)
             .Presets(DatePreset.Last30Days, DatePreset.ThisYear);
            b.Count("orders").Title("Orders");
            b.Sum("revenue", x => x.Amount).Title("Revenue");
            b.Average("average", x => x.Amount).Title("Average order");
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
