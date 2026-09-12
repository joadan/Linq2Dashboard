using Linq2Dashboard.Indexing;

namespace Linq2Dashboard;

/// <summary>
/// An immutable snapshot of the dashboard for one set of selections (concept §4.7, design §2.4).
/// Everything here was calculated from the same selections at the same moment. Pages and items
/// are served from the snapshot without touching the dashboard's mutable state, of which there is none.
/// </summary>
public sealed class DashboardState<T>
{
    private readonly Dashboard<T> dashboard;
    private readonly RowSet matching;
    private readonly Dictionary<string, FacetState> facetsByKey;
    private readonly Dictionary<string, MetricState> metricsByKey;

    internal DashboardState(Dashboard<T> dashboard, Selections selections, RowSet matching, FacetState[] facets, MetricState[] metrics)
    {
        this.dashboard = dashboard;
        Selections = selections;
        this.matching = matching;
        Facets = facets;
        Metrics = metrics;
        facetsByKey = facets.ToDictionary(f => f.Key, StringComparer.Ordinal);
        metricsByKey = metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);
    }

    /// <summary>The selections this state was calculated from.</summary>
    public Selections Selections { get; }

    /// <summary>Rows in the dataset, after fixed filters (concept §4.3).</summary>
    public int TotalCount => dashboard.TotalCount;

    /// <summary>Rows satisfying every current selection (concept §3).</summary>
    public int MatchingCount => matching.Count;

    public IReadOnlyList<FacetState> Facets { get; }

    public IReadOnlyList<MetricState> Metrics { get; }

    public FacetState Facet(string key) =>
        facetsByKey.TryGetValue(key, out FacetState? facet)
            ? facet
            : throw new ArgumentException($"Unknown facet key '{key}'.", nameof(key));

    public bool TryGetFacet(string key, out FacetState facet) => facetsByKey.TryGetValue(key, out facet!);

    public MetricState Metric(string key) =>
        metricsByKey.TryGetValue(key, out MetricState? metric)
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

        return new ResultPage<T>(GetItems((long)pageIndex * pageSize, pageSize), pageIndex, pageSize, MatchingCount);
    }

    /// <summary>
    /// Matching rows <paramref name="skip"/> to <paramref name="skip"/> + <paramref name="take"/> in the
    /// application-defined order. The slice a virtualised list asks for (design §9); a slice beyond
    /// the end is empty.
    /// </summary>
    public IReadOnlyList<T> GetItems(long skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(take);

        if (take == 0 || skip >= MatchingCount)
        {
            return [];
        }

        var items = new List<T>((int)Math.Min(take, MatchingCount - skip));
        foreach (int row in OrderedMatchingRows())
        {
            if (skip > 0)
            {
                skip--;
                continue;
            }

            items.Add(dashboard.ItemAt(row));
            if (items.Count == take)
            {
                break;
            }
        }

        return items;
    }

    /// <summary>All matching rows in the application-defined order, lazily. For export and iteration.</summary>
    public IEnumerable<T> Items
    {
        get
        {
            foreach (int row in OrderedMatchingRows())
            {
                yield return dashboard.ItemAt(row);
            }
        }
    }

    internal RowSet Matching => matching;

    private IEnumerable<int> OrderedMatchingRows()
    {
        int[]? sorted = dashboard.SortedRows;
        if (sorted is null)
        {
            return matching.Rows();
        }

        return Walk(sorted, matching);

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
