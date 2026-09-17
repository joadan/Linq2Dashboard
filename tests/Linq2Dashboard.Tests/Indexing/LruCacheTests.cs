using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Tests.Indexing;

public class LruCacheTests
{
    [Fact]
    public void Capacity_must_be_at_least_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<int, string>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<int, string>(-1));
    }

    [Fact]
    public void A_miss_runs_the_factory_once_and_a_hit_returns_the_same_value()
    {
        var cache = new LruCache<int, object>(4);
        int calls = 0;

        object first = cache.GetOrAdd(1, _ => { calls++; return new object(); });
        object second = cache.GetOrAdd(1, _ => { calls++; return new object(); });

        Assert.Same(first, second);
        Assert.Equal(1, calls);
        Assert.True(cache.TryGet(1, out object? found));
        Assert.Same(first, found);
        Assert.False(cache.TryGet(2, out _));
    }

    [Fact]
    public void The_least_recently_used_entry_is_evicted_beyond_the_capacity()
    {
        var cache = new LruCache<int, int>(2);
        cache.GetOrAdd(1, k => k);
        cache.GetOrAdd(2, k => k);

        cache.GetOrAdd(3, k => k);

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet(1, out _));
        Assert.True(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(3, out _));
    }

    [Fact]
    public void A_hit_counts_as_use_and_protects_the_entry_from_eviction()
    {
        var cache = new LruCache<int, int>(2);
        cache.GetOrAdd(1, k => k);
        cache.GetOrAdd(2, k => k);

        Assert.True(cache.TryGet(1, out _));   // 1 is now the most recent; 2 is the oldest
        cache.GetOrAdd(3, k => k);

        Assert.True(cache.TryGet(1, out _));
        Assert.False(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(3, out _));
    }

    [Fact]
    public void Clear_empties_the_cache_and_a_later_miss_recomputes()
    {
        var cache = new LruCache<int, int>(2);
        cache.GetOrAdd(1, k => k);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(1, out _));
        Assert.Equal(10, cache.GetOrAdd(1, k => k * 10));
    }

    [Fact]
    public void Concurrent_readers_and_writers_never_exceed_the_capacity_or_corrupt_the_cache()
    {
        // The cache is the only shared mutable state in a dashboard (design §5), so it is what the
        // thread-safety claim on Dashboard<T> rests on. Many threads hammer a small cache with
        // overlapping keys; every call must return the value for its own key and the size must stay bounded.
        var cache = new LruCache<int, string>(8);

        Parallel.For(0, 20_000, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount) }, i =>
        {
            int key = i % 32;
            string value = cache.GetOrAdd(key, k => $"value {k}");
            Assert.Equal($"value {key}", value);
            if (cache.TryGet(key, out string? again))
            {
                Assert.Equal(value, again);
            }
        });

        Assert.InRange(cache.Count, 1, 8);
    }

    [Fact]
    public async Task A_miss_computed_twice_under_contention_keeps_the_first_result()
    {
        // The factory runs outside the lock by design; the first value stored wins and the loser is discarded.
        var cache = new LruCache<int, object>(4);
        using var gate = new ManualResetEventSlim(false);
        using var bothInside = new CountdownEvent(2);
        object a = new(), b = new();

        Task<object> first = Task.Run(() => cache.GetOrAdd(1, _ => { bothInside.Signal(); gate.Wait(); return a; }));
        Task<object> second = Task.Run(() => cache.GetOrAdd(1, _ => { bothInside.Signal(); gate.Wait(); return b; }));
        Assert.True(bothInside.Wait(TimeSpan.FromSeconds(5)));   // both factories are running, so both missed
        gate.Set();
        object[] results = await Task.WhenAll(first, second);

        Assert.Same(results[0], results[1]);
        Assert.True(cache.TryGet(1, out object? stored));
        Assert.Same(results[0], stored);
        Assert.Equal(1, cache.Count);
    }
}
