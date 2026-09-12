using Linq2Dashboard.Indexing;

namespace Linq2Dashboard;

/// <summary>
/// An immutable snapshot of the dashboard for one set of selections (concept §4.7, design §2.4).
/// Everything here was calculated from the same selections at the same moment. Pages and items
/// are served from the snapshot without touching the dashboard's mutable state, of which there is none.
/// </summary>
public sealed class DashboardState<T>
{
    private readonly Dashboard<T> _dashboard;
    private readonly RowSet _matching;
    private readonly Dictionary<string, FacetState> _facetsByKey;
    private readonly Dictionary<string, MetricState> _metricsByKey;

    internal DashboardState(Dashboard<T> dashboard, Selections selections, RowSet matching, FacetState[] facets, MetricState[] metrics)
    {
        _dashboard = dashboard;
        Selections = selections;
        _matching = matching;
        Facets = facets;
        Metrics = metrics;
        _facetsByKey = facets.ToDictionary(f => f.Key, StringComparer.Ordinal);
        _metricsByKey = metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);
    }

    /// <summary>The selections this state was calculated from.</summary>
    public Selections Selections { get; }

    /// <summary>Rows in the dataset, after fixed filters (concept §4.3).</summary>
    public int TotalCount => _dashboard.TotalCount;

    /// <summary>Rows satisfying every current selection (concept §3).</summary>
    public int MatchingCount => _matching.Count;

    public IReadOnlyList<FacetState> Facets { get; }

    public IReadOnlyList<MetricState> Metrics { get; }

    public FacetState Facet(string key) =>
        _facetsByKey.TryGetValue(key, out FacetState? facet)
            ? facet
            : throw new ArgumentException($"Unknown facet key '{key}'.", nameof(key));

    public bool TryGetFacet(string key, out FacetState facet) => _facetsByKey.TryGetValue(key, out facet!);

    public MetricState Metric(string key) =>
        _metricsByKey.TryGetValue(key, out MetricState? metric)
            ? metric
            : throw new ArgumentException($"Unknown metric key '{key}'.", nameof(key));

    /// <summary>Number of pages of <paramref name="pageSize"/> needed for all matching rows.</summary>
    public int PageCount(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        return (MatchingCount + pageSize - 1) / pageSize;
    }

    /// <summary>
    /// Matching rows <c>pageIndex * pageSize</c> to <c>(pageIndex + 1) * pageSize</c> in the
    /// application-defined order (design §4.7). A page beyond the end is empty, not an error.
    /// </summary>
    public ResultPage<T> GetPage(int pageIndex, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var items = new List<T>(Math.Min(pageSize, MatchingCount));
        long skip = (long)pageIndex * pageSize;
        foreach (int row in OrderedMatchingRows())
        {
            if (skip > 0)
            {
                skip--;
                continue;
            }

            items.Add(_dashboard.ItemAt(row));
            if (items.Count == pageSize)
            {
                break;
            }
        }

        return new ResultPage<T>(items, pageIndex, pageSize, MatchingCount);
    }

    /// <summary>All matching rows in the application-defined order, lazily. For export and iteration.</summary>
    public IEnumerable<T> Items
    {
        get
        {
            foreach (int row in OrderedMatchingRows())
            {
                yield return _dashboard.ItemAt(row);
            }
        }
    }

    internal RowSet Matching => _matching;

    private IEnumerable<int> OrderedMatchingRows()
    {
        int[]? sorted = _dashboard.SortedRows;
        if (sorted is null)
        {
            return _matching.Rows();
        }

        return Walk(sorted, _matching);

        static IEnumerable<int> Walk(int[] sorted, RowSet matching)
        {
            foreach (int row in sorted)
            {
                if (matching.Contains(row))
                {
                    yield return row;
                }
            }
        }
    }
}
