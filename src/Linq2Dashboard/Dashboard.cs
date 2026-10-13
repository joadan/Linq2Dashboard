using System.ComponentModel;
using Linq2Dashboard.Calculation;
using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Metrics;
using Linq2Dashboard.Queries;

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
[ImmutableObject(true)]
public sealed class Dashboard<T>
{
    private const int RowSetCacheCapacity = 256;
    private const int StateCacheCapacity = 8;

    private readonly T[] items;
    private readonly FacetIndex[] facets;
    private readonly Dictionary<string, FacetIndex> facetsByKey;
    private readonly Dictionary<string, int> facetPositions;
    private readonly MetricIndex[] metrics;
    private readonly Dictionary<string, MetricIndex> metricsByKey;
    private readonly LruCache<(string Key, Selection Selection), RowSet> rowSets;
    private readonly LruCache<Selections, DashboardState<T>> states = new(StateCacheCapacity);

    internal Dashboard(T[] items, FacetIndex[] facets, MetricIndex[] metrics, int[]? sortedRows, TimeProvider timeProvider, bool parallelCounting)
        : this(items, facets, metrics, sortedRows, timeProvider, parallelCounting, RowSet.Full(items.Length), new LruCache<(string Key, Selection Selection), RowSet>(RowSetCacheCapacity), new SelectionSerializer(facets), new PropertyCatalog<T>())
    {
    }

    /// <summary>The scoped dashboard of concept §4.10: the parent's rows, columns, indexes and order, with <paramref name="scope"/> as the dataset.</summary>
    private Dashboard(Dashboard<T> parent, RowSet scope)
        : this(
            parent.items,
            parent.facets.Select(f => f.Scope(scope)).ToArray(),
            parent.metrics.Select(m => m.Scope(scope)).ToArray(),
            parent.SortedRows, parent.TimeProvider, parent.ParallelCounting, scope,
            // Selections mean the same in every scope, and the rows a selection matches do not depend on the scope,
            // so the row-set cache and the serializer are shared with the parent (design §5), as are the compiled
            // property getters of queries (design §10.3).
            parent.rowSets, parent.Serializer, parent.Properties)
    {
    }

    private Dashboard(
        T[] items, FacetIndex[] facets, MetricIndex[] metrics, int[]? sortedRows, TimeProvider timeProvider, bool parallelCounting,
        RowSet all, LruCache<(string Key, Selection Selection), RowSet> rowSets, SelectionSerializer serializer, PropertyCatalog<T> properties)
    {
        Properties = properties;
        this.items = items;
        this.facets = facets;
        facetsByKey = facets.ToDictionary(f => f.Key, StringComparer.Ordinal);
        facetPositions = facets.Select((f, i) => (f.Key, i)).ToDictionary(p => p.Key, p => p.i, StringComparer.Ordinal);
        this.metrics = metrics;
        metricsByKey = metrics.ToDictionary(m => m.Key, StringComparer.Ordinal);
        SortedRows = sortedRows;
        TimeProvider = timeProvider;
        ParallelCounting = parallelCounting;
        All = all;
        this.rowSets = rowSets;
        Facets = facets.Select(f => f.Info).ToArray();
        Metrics = metrics.Select(m => m.Info).ToArray();
        Serializer = serializer;
    }

    /// <summary>Writes and reads <see cref="Selections"/> as JSON for storage and bookmarks (design §2.5). The same for a dashboard and every scope of it.</summary>
    public SelectionSerializer Serializer { get; }

    /// <summary>Rows in the dataset: after fixed filters, and inside the scope for a scoped dashboard (concept §4.3, §4.10).</summary>
    public int TotalCount => All.Count;

    /// <summary>
    /// A dashboard over the rows of this one that pass <paramref name="predicate"/> (concept §4.10). It
    /// shares this dashboard's definitions, columns, indexes and result order and behaves exactly like
    /// one built with <paramref name="predicate"/> as one more fixed filter, except that range and date
    /// buckets and range bounds stay this dashboard's. Costs one pass over the rows plus one count per facet and metric,
    /// not a build. The scope is not a selection: it is invisible to the UI and to serialised selections.
    /// Scopes compose, so a scoped dashboard can be scoped again. This dashboard is unchanged.
    /// </summary>
    public Dashboard<T> ScopeTo(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var builder = new RowSetBuilder(items.Length);
        foreach (int row in All)
        {
            if (predicate(items[row]))
            {
                builder.Set(row);
            }
        }

        return new Dashboard<T>(this, builder.Build());
    }

    /// <summary>
    /// A dashboard over the rows of this one that <paramref name="selections"/> match, resolved by the
    /// facets themselves (concept §4.10): the same rows the selections would match as a user's choice,
    /// with the facets' rules for case, null and intervals, and no call on the row objects. The scoped
    /// dashboard starts with nothing selected; its facets show only the values in scope, without the
    /// own-facet exclusion a selection gets (concept §4.2); a relative date preset is resolved now and
    /// stays fixed. An unknown facet key is an error, as in <see cref="Calculate(Selections)"/>.
    /// </summary>
    public Dashboard<T> ScopeTo(Selections selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        return new Dashboard<T>(this, Matching(selections));
    }

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
        return states.GetOrAdd(selections, s => DashboardCalculator.Calculate(this, s));
    }

    /// <summary>The state with nothing selected.</summary>
    public DashboardState<T> Calculate() => Calculate(Selections.Empty);

    /// <summary>
    /// Answers one question about this dashboard's rows with one table (concept §4.11, design §10): the rows
    /// <see cref="DashboardQuery{T}.Selections"/> match, every selection applied, grouped by up to two
    /// dimensions, each group reporting the query's values, then ordered and cut. Reads the dashboard and
    /// changes nothing: no selection, no state, no cache entry. Thread-safe; any number of queries may run at
    /// once, also beside <see cref="Calculate(Selections)"/>. Every part is checked before a row is read, and a
    /// mistake throws an <see cref="ArgumentException"/> that says what is wrong and what is known.
    /// Synchronous: a query is CPU work, so a host that must keep its thread free runs it on another (design §10.4).
    /// </summary>
    /// <param name="query">The question.</param>
    /// <param name="cancellationToken">Checked between steps and every 65 536 rows; a cancelled query throws <see cref="OperationCanceledException"/>.</param>
    public QueryResult Query(DashboardQuery<T> query, CancellationToken cancellationToken = default) =>
        QueryRunner.Run(this, query, cancellationToken);

    /// <summary>
    /// Reads a query from JSON that comes from outside the application, from an agent or an HTTP client (design §10.6),
    /// and checks it as <see cref="Query"/> would. Strict throughout, selections included: anything that cannot be
    /// read or is not known throws a <see cref="DashboardQueryException"/> whose <see cref="DashboardQueryException.Path"/>
    /// names the part, with a message meant to be passed back as it is. Each part is an object or a short text form:
    /// <code>
    /// {
    ///   "selections": "Country=SE,NO&amp;Status=Open",
    ///   "where": "Amount>=100000",
    ///   "groupBy": ["SalesRep", "OrderDate:Month"],
    ///   "values": ["revenue", "count", "avg:Amount"],
    ///   "orderBy": "-revenue",
    ///   "take": 10
    /// }
    /// </code>
    /// </summary>
    /// <param name="json">The query as JSON text.</param>
    /// <param name="options">The where's parser and the names the caller may use; null allows every name and no where.</param>
    public DashboardQuery<T> QueryFromJson(string json, QueryJsonOptions<T>? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return QueryJsonReader.Read(this, json, options);
    }

    /// <summary>
    /// What a query can ask of this dashboard (design §10.7): facets with what helps a question (the values of a small
    /// facet, a range facet's bounds, a date facet's period, zone and presets), metrics, and the properties of
    /// <typeparamref name="T"/> with what each allows, plus the periods and the limits. For a client or an agent before
    /// it asks; write it with <see cref="SelectionSerializer.ToJson(DashboardDescription, bool)"/>. Give it the same
    /// <see cref="DescribeOptions.Allowed"/> list as the query reader, and the application's descriptions of what values mean.
    /// </summary>
    public DashboardDescription Describe(DescribeOptions? options = null) => QueryDescriber.Describe(this, options ?? new DescribeOptions());

    /// <summary>Reads a query from a JSON object, as <see cref="QueryFromJson(string, QueryJsonOptions{T}?)"/> does, for callers that embed it in a larger document.</summary>
    /// <param name="json">The query as a JSON object.</param>
    /// <param name="options">The where's parser and the names the caller may use; null allows every name and no where.</param>
    public DashboardQuery<T> QueryFromJsonObject(System.Text.Json.Nodes.JsonObject json, QueryJsonOptions<T>? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        return QueryJsonReader.Read(this, json, options);
    }

    /// <summary>Calculates without consulting or filling the state cache. For benchmarks; the row-set cache still applies.</summary>
    internal DashboardState<T> CalculateUncached(Selections selections) => DashboardCalculator.Calculate(this, selections);

    /// <summary>Empties both caches. For benchmarks that need a cold start.</summary>
    internal void ClearCaches()
    {
        rowSets.Clear();
        states.Clear();
    }

    internal ReadOnlySpan<T> Items => items;

    /// <summary>The row array, for a query's where and property reads (design §10.3). Never written after build.</summary>
    internal T[] ItemArray => items;

    /// <summary>The properties of <typeparamref name="T"/> queries can name, with their compiled getters (design §10.5).</summary>
    internal PropertyCatalog<T> Properties { get; }

    internal T ItemAt(int row) => items[row];

    internal IReadOnlyList<FacetIndex> FacetIndexes => facets;

    internal IReadOnlyDictionary<string, int> FacetPositions => facetPositions;

    internal IReadOnlyList<MetricIndex> MetricIndexes => metrics;

    /// <summary>Row ids in the application-defined order, or null for identity order.</summary>
    internal int[]? SortedRows { get; }

    /// <summary>Every row in the dataset: all rows for a dashboard from the builder, the scope for a scoped one (concept §4.10). The calculation starts from this set (design §4.2).</summary>
    internal RowSet All { get; }

    internal FacetIndex FacetIndex(string key) =>
        facetsByKey.TryGetValue(key, out FacetIndex? facet)
            ? facet
            : throw new ArgumentException($"Unknown facet key '{key}'.", nameof(key));

    internal bool TryGetFacetIndex(string key, out FacetIndex facet) => facetsByKey.TryGetValue(key, out facet!);

    internal bool TryGetMetricIndex(string key, out MetricIndex metric) => metricsByKey.TryGetValue(key, out metric!);

    internal MetricIndex MetricIndex(string key) =>
        metricsByKey.TryGetValue(key, out MetricIndex? metric)
            ? metric
            : throw new ArgumentException($"Unknown metric key '{key}'.", nameof(key));

    /// <summary>Rows matching one facet's selection, through the row-set cache (design §5).</summary>
    internal RowSet RowsMatching(FacetIndex facet, Selection selection) =>
        rowSets.GetOrAdd((facet.Key, selection), _ => facet.RowsMatching(selection));

    /// <summary>
    /// The rows <paramref name="selections"/> match within this dashboard, every selection applied (concept §4.11):
    /// the cached state's matching set when there is one, otherwise the cached selection row sets ANDed with
    /// <see cref="All"/>, without counting any facet (design §10.3). An unknown key throws, as in <see cref="Calculate(Selections)"/>.
    /// </summary>
    internal RowSet MatchingRows(Selections selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        return states.TryGet(selections, out DashboardState<T> state) ? state.Matching : Matching(selections);
    }

    private RowSet Matching(Selections selections)
    {
        RowSet rows = All;
        foreach ((string key, Selection selection) in selections)
        {
            if (!facetsByKey.TryGetValue(key, out FacetIndex? facet))
            {
                throw new ArgumentException($"Unknown facet key '{key}'.", nameof(selections));
            }

            rows = rows.And(RowsMatching(facet, selection));
        }

        return rows;
    }

    internal int CachedRowSetCount => rowSets.Count;

    internal int CachedStateCount => states.Count;
}
