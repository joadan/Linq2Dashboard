using Linq2Dashboard.Indexing;
using Linq2Dashboard.State;

namespace Linq2Dashboard;

/// <summary>
/// An immutable snapshot of the dashboard for one set of selections (concept §4.7, design §2.4).
/// Everything here was calculated from the same selections at the same moment. The matching rows
/// are served from the snapshot without touching the dashboard's mutable state, of which there is none.
/// </summary>
public sealed class DashboardState<T>
{
    private readonly Dashboard<T> dashboard;
    private readonly RowSet matching;
    private readonly Dictionary<string, FacetState> facetsByKey;
    private readonly Dictionary<string, MetricState> metricsByKey;
    private readonly MatchingItems<T> items;

    internal DashboardState(Dashboard<T> dashboard, Selections selections, RowSet matching, FacetState[] facets, MetricState[] metrics)
    {
        this.dashboard = dashboard;
        Selections = selections;
        this.matching = matching;
        Facets = facets;
        Metrics = metrics;
        facetsByKey = facets.ToDictionary(f => f.Key, StringComparer.Ordinal);
        metricsByKey = metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);
        items = new MatchingItems<T>(dashboard, matching);
    }

    /// <summary>The selections this state was calculated from.</summary>
    public Selections Selections { get; }

    /// <summary>Rows in the dataset, after fixed filters (concept §4.3).</summary>
    public int TotalCount => dashboard.TotalCount;

    /// <summary>Rows satisfying every current selection (concept §3).</summary>
    public int MatchingCount => matching.Count;

    /// <summary>Every facet's state, in definition order.</summary>
    public IReadOnlyList<FacetState> Facets { get; }

    /// <summary>Every metric's state, in definition order.</summary>
    public IReadOnlyList<MetricState> Metrics { get; }

    /// <summary>The state of the facet with <paramref name="key"/>. Throws for an unknown key: keys are a code path, not external input.</summary>
    public FacetState Facet(string key) =>
        facetsByKey.TryGetValue(key, out FacetState? facet)
            ? facet
            : throw new ArgumentException($"Unknown facet key '{key}'.", nameof(key));

    /// <summary>Gets the state of the facet with <paramref name="key"/>; false for an unknown key.</summary>
    public bool TryGetFacet(string key, out FacetState facet) => facetsByKey.TryGetValue(key, out facet!);

    /// <summary>The state of the metric with <paramref name="key"/>. Throws for an unknown key.</summary>
    public MetricState Metric(string key) =>
        metricsByKey.TryGetValue(key, out MetricState? metric)
            ? metric
            : throw new ArgumentException($"Unknown metric key '{key}'.", nameof(key));

    /// <summary>
    /// Matching rows <paramref name="skip"/> to <paramref name="skip"/> + <paramref name="take"/> in the
    /// application-defined order (design §4.7): the slice a paged or virtualised grid asks for. A slice
    /// beyond the end is empty, not an error. The same rows as <c>Items.Skip(skip).Take(take)</c>.
    /// </summary>
    public IReadOnlyList<T> GetItems(long skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        return items.Slice(skip, take);
    }

    /// <summary>
    /// All matching rows in the application-defined order (concept §3, design §4.7), as a read-only list:
    /// counted, indexable and enumerable, so a data grid given it, or <c>Items.AsQueryable()</c>, pages,
    /// virtualises, sorts and counts without walking the whole set on every request. The order is
    /// materialised on first use, four bytes per matching row; enumeration for export costs no more.
    /// </summary>
    public IReadOnlyList<T> Items => items;

    internal RowSet Matching => matching;
}
