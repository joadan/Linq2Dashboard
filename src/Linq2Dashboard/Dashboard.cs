using Linq2Dashboard.Facets;
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
/// state: the UI owns the current <see cref="Selections"/> and asks for a new state on every change.
/// </summary>
public sealed class Dashboard<T>
{
    private readonly T[] _items;
    private readonly FacetIndex[] _facets;
    private readonly Dictionary<string, FacetIndex> _facetsByKey;
    private readonly MetricIndex[] _metrics;
    private readonly Dictionary<string, MetricIndex> _metricsByKey;

    internal Dashboard(T[] items, FacetIndex[] facets, MetricIndex[] metrics, int[]? sortedRows, TimeProvider timeProvider)
    {
        _items = items;
        _facets = facets;
        _facetsByKey = facets.ToDictionary(f => f.Key, StringComparer.Ordinal);
        _metrics = metrics;
        _metricsByKey = metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);
        SortedRows = sortedRows;
        TimeProvider = timeProvider;
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

    internal ReadOnlySpan<T> Items => _items;

    internal T ItemAt(int row) => _items[row];

    internal IReadOnlyList<FacetIndex> FacetIndexes => _facets;

    internal IReadOnlyList<MetricIndex> MetricIndexes => _metrics;

    /// <summary>Row ids in the application-defined order, or null for identity order.</summary>
    internal int[]? SortedRows { get; }

    internal FacetIndex FacetIndex(string key) =>
        _facetsByKey.TryGetValue(key, out FacetIndex? facet)
            ? facet
            : throw new ArgumentException($"Unknown facet key '{key}'.", nameof(key));

    internal bool TryGetFacetIndex(string key, out FacetIndex facet) => _facetsByKey.TryGetValue(key, out facet!);

    internal MetricIndex MetricIndex(string key) =>
        _metricsByKey.TryGetValue(key, out MetricIndex? metric)
            ? metric
            : throw new ArgumentException($"Unknown metric key '{key}'.", nameof(key));
}
