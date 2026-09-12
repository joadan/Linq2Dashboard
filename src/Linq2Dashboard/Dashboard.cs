using Linq2Dashboard.Calculation;
using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Metrics;

namespace Linq2Dashboard;

/// <summary>Entry point: builds a <see cref="Dashboard{T}"/> from a collection (design §2.1).</summary>
public static class Dashboard
{
    /// <summary>
    /// Enumerates <paramref name="source"/> exactly once, applies the fixed filters, and builds every
    /// column and index. The returned dashboard is immutable and thread-safe (concept §4.9).
    /// </summary>
    public static Dashboard<T> Create<T>(IEnumerable<T> source, Action<DashboardBuilder<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new DashboardBuilder<T>();
        configure(builder);
        return builder.Build(source);
    }
}

/// <summary>
/// An immutable dataset with its facets, metrics and result order (concept §3). Holds no selection
/// state: the UI owns the current <see cref="Selections"/> and asks for a new
/// <see cref="DashboardState{T}"/> on every change (concept §2). Thread-safe.
/// </summary>
public sealed class Dashboard<T>
{
    private const int RowSetCacheCapacity = 256;
    private const int StateCacheCapacity = 8;

    private readonly T[] _items;
    private readonly FacetIndex[] _facets;
    private readonly Dictionary<string, FacetIndex> _facetsByKey;
    private readonly Dictionary<string, int> _facetPositions;
    private readonly MetricIndex[] _metrics;
    private readonly Dictionary<string, MetricIndex> _metricsByKey;
    private readonly LruCache<(string Key, Selection Selection), RowSet> _rowSets = new(RowSetCacheCapacity);
    private readonly LruCache<Selections, DashboardState<T>> _states = new(StateCacheCapacity);

    internal Dashboard(T[] items, FacetIndex[] facets, MetricIndex[] metrics, int[]? sortedRows, TimeProvider timeProvider, bool parallelCounting)
    {
        _items = items;
        _facets = facets;
        _facetsByKey = facets.ToDictionary(f => f.Key, StringComparer.Ordinal);
        _facetPositions = facets.Select((f, i) => (f.Key, i)).ToDictionary(p => p.Key, p => p.i, StringComparer.Ordinal);
        _metrics = metrics;
        _metricsByKey = metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);
        SortedRows = sortedRows;
        TimeProvider = timeProvider;
        ParallelCounting = parallelCounting;
        All = RowSet.Full(items.Length);
        Facets = facets.Select(f => f.Info).ToArray();
        Metrics = metrics.Select(m => m.Info).ToArray();
    }

    /// <summary>Rows in the dataset, after fixed filters (concept §4.3).</summary>
    public int TotalCount => _items.Length;

    /// <summary>The facets in definition order.</summary>
    public IReadOnlyList<FacetInfo> Facets { get; }

    /// <summary>The metrics in definition order.</summary>
    public IReadOnlyList<MetricInfo> Metrics { get; }

    /// <summary>Source of "now" for relative date presets (concept §5).</summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>Whether facets are counted on separate threads (design §4.3). Off by default.</summary>
    public bool ParallelCounting { get; }

    /// <summary>
    /// The dashboard for <paramref name="selections"/>: counts, facet values, metrics and access to
    /// the matching rows, all from the same selections (concept §4.7). Pure and thread-safe; equal
    /// selections give equal states, and recent states are cached (design §5).
    /// </summary>
    public DashboardState<T> Calculate(Selections selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        return _states.GetOrAdd(selections, s => DashboardCalculator.Calculate(this, s));
    }

    /// <summary>The state with nothing selected.</summary>
    public DashboardState<T> Calculate() => Calculate(Selections.Empty);

    internal ReadOnlySpan<T> Items => _items;

    internal T ItemAt(int row) => _items[row];

    internal IReadOnlyList<FacetIndex> FacetIndexes => _facets;

    internal IReadOnlyDictionary<string, int> FacetPositions => _facetPositions;

    internal IReadOnlyList<MetricIndex> MetricIndexes => _metrics;

    /// <summary>Row ids in the application-defined order, or null for identity order.</summary>
    internal int[]? SortedRows { get; }

    /// <summary>Every row in the dataset.</summary>
    internal RowSet All { get; }

    internal FacetIndex FacetIndex(string key) =>
        _facetsByKey.TryGetValue(key, out FacetIndex? facet)
            ? facet
            : throw new ArgumentException($"Unknown facet key '{key}'.", nameof(key));

    internal bool TryGetFacetIndex(string key, out FacetIndex facet) => _facetsByKey.TryGetValue(key, out facet!);

    internal MetricIndex MetricIndex(string key) =>
        _metricsByKey.TryGetValue(key, out MetricIndex? metric)
            ? metric
            : throw new ArgumentException($"Unknown metric key '{key}'.", nameof(key));

    /// <summary>Rows matching one facet's selection, through the row-set cache (design §5).</summary>
    internal RowSet RowsMatching(FacetIndex facet, Selection selection) =>
        _rowSets.GetOrAdd((facet.Key, selection), _ => facet.RowsMatching(selection));

    internal int CachedRowSetCount => _rowSets.Count;

    internal int CachedStateCount => _states.Count;
}
