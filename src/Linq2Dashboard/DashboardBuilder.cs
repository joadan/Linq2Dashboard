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
    private readonly List<Func<T, bool>> filters = [];
    private readonly List<FacetDefinition<T>> facets = [];
    private readonly List<MetricDefinition<T>> metrics = [];
    private readonly List<SortKey<T>> sort = [];
    private TimeProvider timeProvider = TimeProvider.System;
    private bool parallelCounting;
    private bool built;

    internal DashboardBuilder()
    {
    }

    /// <summary>A fixed filter (concept §3). Rows failing it are not part of the dataset. Several are combined with AND.</summary>
    public DashboardBuilder<T> Where(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        EnsureMutable();
        filters.Add(predicate);
        return this;
    }

    /// <summary>A value facet keyed by the selector's member name (concept §7).</summary>
    public ValueFacetBuilder<T, TProp> ValueFacet<TProp>(Expression<Func<T, TProp>> selector) =>
        ValueFacet(DeriveKey(selector), selector);

    /// <summary>A value facet with an explicit key.</summary>
    public ValueFacetBuilder<T, TProp> ValueFacet<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddValueFacet(key, selector, FacetKind.Value);

    /// <summary>A boolean facet keyed by the selector's member name: the values true and false (concept §5).</summary>
    public ValueFacetBuilder<T, bool> BooleanFacet(Expression<Func<T, bool>> selector) =>
        BooleanFacet(DeriveKey(selector), selector);

    /// <summary>A boolean facet with an explicit key.</summary>
    public ValueFacetBuilder<T, bool> BooleanFacet(string key, Expression<Func<T, bool>> selector) =>
        AddValueFacet(key, selector, FacetKind.Boolean);

    /// <summary>A boolean facet over a nullable member, keyed by its name. Null is a third value (concept §4.8).</summary>
    public ValueFacetBuilder<T, bool?> BooleanFacet(Expression<Func<T, bool?>> selector) =>
        BooleanFacet(DeriveKey(selector), selector);

    /// <summary>A boolean facet over a nullable member, with an explicit key.</summary>
    public ValueFacetBuilder<T, bool?> BooleanFacet(string key, Expression<Func<T, bool?>> selector) =>
        AddValueFacet(key, selector, FacetKind.Boolean);

    /// <summary>A numeric range facet. <typeparamref name="TProp"/> must be a numeric type or its nullable form.</summary>
    public RangeFacetBuilder<T> RangeFacet<TProp>(Expression<Func<T, TProp>> selector) =>
        RangeFacet(DeriveKey(selector), selector);

    /// <summary>A numeric range facet with an explicit key. <typeparamref name="TProp"/> must be a numeric type or its nullable form.</summary>
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

    /// <summary>A date facet with an explicit key. <typeparamref name="TDate"/> must be <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/> or one of their nullable forms.</summary>
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
            name => definition.Name = name,
            zone => definition.Zone = zone,
            granularity => definition.Granularity = granularity,
            maxPeriods => definition.MaxPeriods = maxPeriods,
            presets => definition.Presets = presets,
            skip => definition.SkipEmptyPresets = skip,
            EnsureMutable);
    }

    /// <summary>
    /// A text facet (concept §5): free text the user types, matched row by row by
    /// <paramref name="predicate"/>, called as <c>(row, text)</c> with the trimmed text. The
    /// predicate owns the matching semantics and must be pure and thread-safe, since the scan runs
    /// on several threads when parallel counting is enabled. The key is always explicit because
    /// there is no selector to derive one from.
    /// </summary>
    /// <remarks>
    /// This is the one potentially expensive operation in the library (design §8): every other facet
    /// counts through a column lookup, but each new text calls <paramref name="predicate"/> once per
    /// row in the dataset. Only a new text pays; the matching rows are cached by the text afterwards.
    /// Keep the predicate cheap, and call <see cref="EnableParallelCounting(bool)"/> for large
    /// datasets so the scan runs on several cores.
    /// </remarks>
    public TextFacetBuilder<T> TextFacet(string key, Func<T, string, bool> predicate)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(predicate);
        EnsureMutable();

        var definition = new TextFacetDefinition<T>(key, predicate);
        AddFacet(definition);
        return new TextFacetBuilder<T>(definition, EnsureMutable);
    }

    /// <summary>Number of matching rows (concept §4.4).</summary>
    public MetricBuilder<T> CountMetric(string key) => AddMetric(key, Aggregation.Count, null);

    /// <summary>Sum of <paramref name="selector"/> over the matching rows, skipping null (concept §4.4). Any numeric type or its nullable form.</summary>
    public MetricBuilder<T> SumMetric<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddMetric(key, Aggregation.Sum, NumericConversion.ToNullableDouble(selector, nameof(selector)));

    /// <summary>Average of <paramref name="selector"/> over the matching rows that have a value, not over all matching rows (concept §4.4).</summary>
    public MetricBuilder<T> AverageMetric<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddMetric(key, Aggregation.Average, NumericConversion.ToNullableDouble(selector, nameof(selector)));

    /// <summary>Smallest non-null value of <paramref name="selector"/> among the matching rows (concept §4.4).</summary>
    public MetricBuilder<T> MinMetric<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddMetric(key, Aggregation.Min, NumericConversion.ToNullableDouble(selector, nameof(selector)));

    /// <summary>Largest non-null value of <paramref name="selector"/> among the matching rows (concept §4.4).</summary>
    public MetricBuilder<T> MaxMetric<TProp>(string key, Expression<Func<T, TProp>> selector) =>
        AddMetric(key, Aggregation.Max, NumericConversion.ToNullableDouble(selector, nameof(selector)));

    /// <summary>
    /// Number of distinct non-null values of <paramref name="selector"/> among the matching rows
    /// (concept §4.4). Equality follows the value facet rules: strings ignore case unless
    /// <paramref name="comparer"/> is given.
    /// </summary>
    public MetricBuilder<T> DistinctMetric<TProp>(string key, Expression<Func<T, TProp>> selector, IEqualityComparer<TProp>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Func<T, TProp> read = selector.Compile();
        return AddMetric(new MetricDefinition<T>(key, items => DistinctColumn.Build(items.Length, (int row, out TProp value) =>
        {
            value = read(items[row]);
            return value is not null;
        }, comparer)));
    }

    /// <summary>
    /// A metric computed from the metrics defined before it, for example
    /// <c>m => m["revenue"] / m["orders"]</c> (concept §4.4). No value when any input has none or the
    /// result is not finite, so a division by zero is "no value", never infinity. The formula is run
    /// once here with every earlier metric at "no value", so a key it reads unconditionally is
    /// checked now; a key first read inside a branch is checked at the first calculation that
    /// reaches it.
    /// </summary>
    public MetricBuilder<T> CalculatedMetric(string key, Func<MetricValues, double?> formula)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(formula);
        EnsureMutable();

        var earlier = new MetricState[metrics.Count];
        for (int i = 0; i < earlier.Length; i++)
        {
            earlier[i] = new MetricState(metrics[i].Key, metrics[i].Name, metrics[i].Aggregation, null, null);
        }

        formula(new MetricValues(earlier, earlier.Length));
        return AddMetric(new MetricDefinition<T>(key, formula));
    }

    /// <summary>Primary result order (concept §8). Call once; add keys with <see cref="ThenBy{TKey}"/>.</summary>
    public DashboardBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, IComparer<TKey>? comparer = null) =>
        AddSortKey(keySelector, descending: false, comparer, primary: true);

    /// <summary>Primary result order, descending (concept §8). Call once; add keys with <see cref="ThenBy{TKey}"/> or <see cref="ThenByDescending{TKey}"/>.</summary>
    public DashboardBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, IComparer<TKey>? comparer = null) =>
        AddSortKey(keySelector, descending: true, comparer, primary: true);

    /// <summary>A further ascending sort key, applied within rows equal on the earlier keys. Requires a preceding <see cref="OrderBy{TKey}"/> or <see cref="OrderByDescending{TKey}"/>.</summary>
    public DashboardBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector, IComparer<TKey>? comparer = null) =>
        AddSortKey(keySelector, descending: false, comparer, primary: false);

    /// <summary>A further descending sort key, applied within rows equal on the earlier keys.</summary>
    public DashboardBuilder<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector, IComparer<TKey>? comparer = null) =>
        AddSortKey(keySelector, descending: true, comparer, primary: false);

    /// <summary>Source of "now" for relative date presets (concept §5). Default is the system clock.</summary>
    public DashboardBuilder<T> UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        EnsureMutable();
        this.timeProvider = timeProvider;
        return this;
    }

    /// <summary>
    /// Count facets on separate threads during <see cref="Dashboard{T}.Calculate(Selections)"/>
    /// (design §4.3). Off by default: it shortens one calculation but occupies several cores per
    /// click, which can hurt a busy server. The same switch spreads a text facet's predicate scan
    /// over the cores, which is where it matters most, since that scan is the one cost the library
    /// does not own.
    /// </summary>
    public DashboardBuilder<T> EnableParallelCounting(bool enabled = true)
    {
        EnsureMutable();
        parallelCounting = enabled;
        return this;
    }

    internal Dashboard<T> Build(IEnumerable<T> source)
    {
        EnsureMutable();
        built = true;

        T[] items = Materialize(source);

        var facetIndexes = new FacetIndex[facets.Count];
        for (int i = 0; i < facetIndexes.Length; i++)
        {
            facetIndexes[i] = facets[i].Build(items, timeProvider, parallelCounting);
        }

        var metricIndexes = new MetricIndex[metrics.Count];
        for (int i = 0; i < metricIndexes.Length; i++)
        {
            metricIndexes[i] = metrics[i].Build(items);
        }

        int[]? sortedRows = SortOrder.Build(items, sort);
        return new Dashboard<T>(items, facetIndexes, metricIndexes, sortedRows, timeProvider, parallelCounting);
    }

    private T[] Materialize(IEnumerable<T> source)
    {
        if (filters.Count == 0)
        {
            return source.ToArray();
        }

        var kept = new List<T>();
        foreach (T item in source)
        {
            bool passes = true;
            foreach (Func<T, bool> filter in filters)
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

    private MetricBuilder<T> AddMetric(string key, Aggregation aggregation, Func<T, double?>? selector) =>
        AddMetric(new MetricDefinition<T>(key, aggregation, selector));

    private MetricBuilder<T> AddMetric(MetricDefinition<T> definition)
    {
        ValidateKey(definition.Key);
        EnsureMutable();
        if (metrics.Any(m => m.Key == definition.Key))
        {
            throw new ArgumentException($"A metric with key '{definition.Key}' is already defined. Keys are case-sensitive and must be unique.", "key");
        }

        metrics.Add(definition);
        return new MetricBuilder<T>(definition, EnsureMutable);
    }

    private DashboardBuilder<T> AddSortKey<TKey>(Expression<Func<T, TKey>> keySelector, bool descending, IComparer<TKey>? comparer, bool primary)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        EnsureMutable();
        if (primary && sort.Count > 0)
        {
            throw new InvalidOperationException("OrderBy has already been called. Use ThenBy or ThenByDescending to add further keys.");
        }

        if (!primary && sort.Count == 0)
        {
            throw new InvalidOperationException("Call OrderBy or OrderByDescending before ThenBy.");
        }

        sort.Add(new SortKey<T, TKey>(keySelector.Compile(), descending, comparer));
        return this;
    }

    private void AddFacet(FacetDefinition<T> definition)
    {
        if (facets.Any(f => f.Key == definition.Key))
        {
            throw new ArgumentException(
                $"A facet with key '{definition.Key}' is already defined. Keys are case-sensitive and must be unique.", "key");
        }

        facets.Add(definition);
    }

    private static string DeriveKey(LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return SelectorKey.Derive(selector, nameof(selector));
    }

    private static void ValidateKey(string key) => ArgumentException.ThrowIfNullOrWhiteSpace(key);

    private void EnsureMutable()
    {
        if (built)
        {
            throw new InvalidOperationException("The dashboard has been built; its configuration can no longer be changed.");
        }
    }
}
