namespace Linq2Dashboard.Benchmarks;

/// <summary>One row of the benchmark dataset (design §8). A class, so a million of them behave like real domain objects.</summary>
public sealed class BenchmarkOrder
{
    public int Id { get; init; }

    public string Country { get; init; } = "";

    public string Status { get; init; } = "";

    public string Category { get; init; } = "";

    public string Brand { get; init; } = "";

    public string Customer { get; init; } = "";

    public bool? IsActive { get; init; }

    public decimal? Amount { get; init; }

    public DateTime? OrderDate { get; init; }

    public int Quantity { get; init; }
}

/// <summary>
/// Deterministic generator for the dataset in design §8: skewed value distributions so the top N
/// facets have a real head and tail, and nulls at the specified rates.
/// </summary>
public static class OrderGenerator
{
    public const int CountryCount = 20;
    public const int StatusCount = 5;
    public const int CategoryCount = 200;
    public const int BrandCount = 2_000;
    public const int CustomerCount = 100_000;

    private static readonly string[] Countries = Enumerable.Range(0, CountryCount).Select(i => $"C{i:00}").ToArray();
    private static readonly string[] Statuses = ["Open", "Pending", "Shipped", "Closed", "Cancelled"];
    private static readonly string[] Categories = Enumerable.Range(0, CategoryCount).Select(i => $"Category {i:000}").ToArray();
    private static readonly string[] Brands = Enumerable.Range(0, BrandCount).Select(i => $"Brand {i:0000}").ToArray();
    private static readonly string[] Customers = Enumerable.Range(0, CustomerCount).Select(i => $"Customer {i:000000}").ToArray();

    private static readonly DateTime RangeStart = new(2023, 1, 1);
    private const int RangeMinutes = 3 * 365 * 24 * 60;

    public static BenchmarkOrder[] Generate(int count, int seed = 20260912)
    {
        var random = new Random(seed);
        var orders = new BenchmarkOrder[count];
        for (int i = 0; i < count; i++)
        {
            orders[i] = new BenchmarkOrder
            {
                Id = i + 1,
                Country = Countries[Skewed(random, CountryCount)],
                Status = Statuses[Skewed(random, StatusCount)],
                Category = Categories[Skewed(random, CategoryCount)],
                Brand = Brands[Skewed(random, BrandCount)],
                Customer = Customers[Skewed(random, CustomerCount)],
                IsActive = random.Next(100) < 2 ? null : random.Next(100) < 80,
                Amount = random.Next(100) < 5 ? null : Math.Round((decimal)(Math.Exp(Gaussian(random) * 1.2) * 150), 2),
                OrderDate = random.Next(100) < 2 ? null : RangeStart.AddMinutes(random.Next(RangeMinutes)),
                Quantity = 1 + Skewed(random, 20),
            };
        }

        return orders;
    }

    /// <summary>Index in [0, n) with a head-heavy distribution: index 0 is the most common.</summary>
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
