using System.Linq.Expressions;
using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Metrics;

namespace Linq2Dashboard;

/// <summary>
/// Configures a dashboard: fixed filters, facets, metrics, result order and time source
/// (design §2.1). Every method validates eagerly so mistakes surface inside
/// <see cref="Dashboard.Create{T}"/>, not at first use. Frozen once the dashboard is built.
/// </summary>
public sealed class DashboardBuilder<T>
{
    private readonly List<Func<T, bool>> _filters = [];
    private readonly List<FacetDefinition<T>> _facets = [];
    private readonly List<MetricDefinition<T>> _metrics = [];
    private readonly List<SortKey<T>> _sort = [];
    private TimeProvider _timeProvider = TimeProvider.System;
    private bool _parallelCounting;
    private bool _built;

    internal DashboardBuilder()
    {
    }

    /// <summary>A fixed filter (concept §3). Rows failing it are not part of the dataset. Several are combined with AND.</summary>
    public DashboardBuilder<T> Where(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        EnsureMutable();
        _filters.Add(predicate);
        return this;
    }

    /// <summary>A value facet keyed by the selector's member name (concept §7).</summary>
    public ValueFacetBuilder<T, TProp> ValueFacet<TProp>(Expression<Func<T, TProp>> selector) =>
        ValueFacet(DeriveKey(selector), selector);

    /// <summary>A value facet with an explicit key.</summary>
    public ValueFacetBuilder<T, TProp> ValueFacet<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddValueFacet(key, selector, FacetKind.Value);

    public ValueFacetBuilder<T, bool> BooleanFacet(Expression<Func<T, bool>> selector) =>
        BooleanFacet(DeriveKey(selector), selector);

    public ValueFacetBuilder<T, bool> BooleanFacet(string key, Expression<Func<T, bool>> selector) =>
        AddValueFacet(key, selector, FacetKind.Boolean);

    public ValueFacetBuilder<T, bool?> BooleanFacet(Expression<Func<T, bool?>> selector) =>
        BooleanFacet(DeriveKey(selector), selector);

    public ValueFacetBuilder<T, bool?> BooleanFacet(string key, Expression<Func<T, bool?>> selector) =>
        AddValueFacet(key, selector, FacetKind.Boolean);

    /// <summary>A numeric range facet. <typeparamref name="TProp"/> must be a numeric type or its nullable form.</summary>
    public RangeFacetBuilder<T> RangeFacet<TProp>(Expression<Func<T, TProp>> selector) =>
        RangeFacet(DeriveKey(selector), selector);

    public RangeFacetBuilder<T> RangeFacet<TProp>(string key, Expression<Func<T, TProp>> selector)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(selector);
        EnsureMutable();

        Func<T, double?> read = NumericConversion.ToNullableDouble(selector, nameof(selector));
        var definition = new RangeFacetDefinition<T>(key, read);
        AddFacet(definition);
        return new RangeFacetBuilder<T>(definition, EnsureMutable);
    }

    /// <summary>
    /// A date facet. <typeparamref name="TDate"/> must be <see cref="DateTime"/>,
    /// <see cref="DateTimeOffset"/>, <see cref="DateOnly"/> or one of their nullable forms.
    /// </summary>
    public DateFacetBuilder<T> DateFacet<TDate>(Expression<Func<T, TDate>> selector) =>
        DateFacet(DeriveKey(selector), selector);

    public DateFacetBuilder<T> DateFacet<TDate>(string key, Expression<Func<T, TDate>> selector)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(selector);
        EnsureMutable();
        if (!DateConversion.IsSupported(typeof(TDate)))
        {
            throw new ArgumentException(
                $"'{typeof(TDate)}' is not a supported date type. Use DateTime, DateTimeOffset or DateOnly, or their nullable forms.",
                nameof(selector));
        }

        var definition = new DateFacetDefinition<T, TDate>(key, selector);
        AddFacet(definition);
        return new DateFacetBuilder<T>(
            key,
            title => definition.Title = title,
            zone => definition.Zone = zone,
            granularity => definition.Granularity = granularity,
            presets => definition.Presets = presets,
            EnsureMutable);
    }

    /// <summary>Number of matching rows (concept §4.4).</summary>
    public MetricBuilder<T> Count(string key) => AddMetric(key, Aggregation.Count, null);

    public MetricBuilder<T> Sum<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddMetric(key, Aggregation.Sum, NumericConversion.ToNullableDouble(selector, nameof(selector)));

    public MetricBuilder<T> Average<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddMetric(key, Aggregation.Average, NumericConversion.ToNullableDouble(selector, nameof(selector)));

    public MetricBuilder<T> Min<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddMetric(key, Aggregation.Min, NumericConversion.ToNullableDouble(selector, nameof(selector)));

    public MetricBuilder<T> Max<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddMetric(key, Aggregation.Max, NumericConversion.ToNullableDouble(selector, nameof(selector)));

    /// <summary>Primary result order (concept §8). Call once; add keys with <see cref="ThenBy{TKey}"/>.</summary>
    public DashboardBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, IComparer<TKey>? comparer = null) =>
        AddSortKey(keySelector, descending: false, comparer, primary: true);

    public DashboardBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, IComparer<TKey>? comparer = null) =>
        AddSortKey(keySelector, descending: true, comparer, primary: true);

    public DashboardBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector, IComparer<TKey>? comparer = null) =>
        AddSortKey(keySelector, descending: false, comparer, primary: false);

    public DashboardBuilder<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector, IComparer<TKey>? comparer = null) =>
        AddSortKey(keySelector, descending: true, comparer, primary: false);

    /// <summary>Source of "now" for relative date presets (concept §5). Default is the system clock.</summary>
    public DashboardBuilder<T> UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        EnsureMutable();
        _timeProvider = timeProvider;
        return this;
    }

    /// <summary>
    /// Count facets on separate threads during <see cref="Dashboard{T}.Calculate(Selections)"/>
    /// (design §4.3). Off by default: it shortens one calculation but occupies several cores per
    /// click, which can hurt a busy server.
    /// </summary>
    public DashboardBuilder<T> EnableParallelCounting(bool enabled = true)
    {
        EnsureMutable();
        _parallelCounting = enabled;
        return this;
    }

    internal Dashboard<T> Build(IEnumerable<T> source)
    {
        EnsureMutable();
        _built = true;

        T[] items = Materialize(source);

        var facets = new FacetIndex[_facets.Count];
        for (int i = 0; i < facets.Length; i++)
        {
            facets[i] = _facets[i].Build(items, _timeProvider);
        }

        var metrics = new MetricIndex[_metrics.Count];
        for (int i = 0; i < metrics.Length; i++)
        {
            metrics[i] = _metrics[i].Build(items);
        }

        int[]? sortedRows = SortOrder.Build(items, _sort);
        return new Dashboard<T>(items, facets, metrics, sortedRows, _timeProvider, _parallelCounting);
    }

    private T[] Materialize(IEnumerable<T> source)
    {
        if (_filters.Count == 0)
        {
            return source.ToArray();
        }

        var kept = new List<T>();
        foreach (T item in source)
        {
            bool passes = true;
            foreach (Func<T, bool> filter in _filters)
            {
                if (!filter(item))
                {
                    passes = false;
                    break;
                }
            }

            if (passes)
            {
                kept.Add(item);
            }
        }

        return kept.ToArray();
    }

    private ValueFacetBuilder<T, TProp> AddValueFacet<TProp>(string key, Expression<Func<T, TProp>> selector, FacetKind kind)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(selector);
        EnsureMutable();

        var definition = new ValueFacetDefinition<T, TProp>(key, selector, kind);
        AddFacet(definition);
        return new ValueFacetBuilder<T, TProp>(definition, EnsureMutable);
    }

    private MetricBuilder<T> AddMetric(string key, Aggregation aggregation, Func<T, double?>? selector)
    {
        ValidateKey(key);
        EnsureMutable();
        if (_metrics.Any(m => m.Key == key))
        {
            throw new ArgumentException($"A metric with key '{key}' is already defined. Keys are case-sensitive and must be unique.", nameof(key));
        }

        var definition = new MetricDefinition<T>(key, aggregation, selector);
        _metrics.Add(definition);
        return new MetricBuilder<T>(definition, EnsureMutable);
    }

    private DashboardBuilder<T> AddSortKey<TKey>(Expression<Func<T, TKey>> keySelector, bool descending, IComparer<TKey>? comparer, bool primary)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        EnsureMutable();
        if (primary && _sort.Count > 0)
        {
            throw new InvalidOperationException("OrderBy has already been called. Use ThenBy or ThenByDescending to add further keys.");
        }

        if (!primary && _sort.Count == 0)
        {
            throw new InvalidOperationException("Call OrderBy or OrderByDescending before ThenBy.");
        }

        _sort.Add(new SortKey<T, TKey>(keySelector.Compile(), descending, comparer));
        return this;
    }

    private void AddFacet(FacetDefinition<T> definition)
    {
        if (_facets.Any(f => f.Key == definition.Key))
        {
            throw new ArgumentException(
                $"A facet with key '{definition.Key}' is already defined. Keys are case-sensitive and must be unique.", "key");
        }

        _facets.Add(definition);
    }

    private static string DeriveKey(LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return SelectorKey.Derive(selector, nameof(selector));
    }

    private static void ValidateKey(string key) => ArgumentException.ThrowIfNullOrWhiteSpace(key);

    private void EnsureMutable()
    {
        if (_built)
        {
            throw new InvalidOperationException("The dashboard has been built; its configuration can no longer be changed.");
        }
    }
}
