using Linq2Dashboard.SampleData;
using Microsoft.Extensions.Caching.Hybrid;

namespace Linq2Dashboard.Sample;

/// <summary>
/// The shared dashboard, built on the first request and cached (concept §7, design §5). It is immutable and thread-safe,
/// so one instance serves every user until the entry expires; the next request after that builds a new one over fresh
/// rows. <see cref="HybridCache"/> makes concurrent first requests share one build and does not cache a failed load.
/// The page that builds its own dashboard from a small dataset, <c>/small</c>, needs none of this.
/// </summary>
public sealed class OrdersDashboardService(HybridCache cache)
{
    private const string Key = "orders-dashboard";

    // A dashboard is an in-memory index and cannot be serialised, so it must never reach a distributed cache.
    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        Flags = HybridCacheEntryFlags.DisableDistributedCache,
    };

    /// <summary>The dashboard over every order, from the cache or built now.</summary>
    public ValueTask<Dashboard<SampleOrder>> GetAsync() =>
        cache.GetOrCreateAsync(
            Key,
            // Generating 200 000 rows stands in for loading them; both it and the build run off the request's thread.
            static cancellationToken => new ValueTask<Dashboard<SampleOrder>>(
                Task.Run(() => SampleOrders.BuildDashboard(rows: 200_000), cancellationToken)),
            Options);

    /// <summary>Drops the cached dashboard, so the next request builds a new one, for example after the orders changed.</summary>
    public ValueTask ClearAsync() => cache.RemoveAsync(Key);
}
