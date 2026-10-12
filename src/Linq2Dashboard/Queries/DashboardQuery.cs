namespace Linq2Dashboard;

/// <summary>
/// One question asked of a dashboard's rows, answered by <see cref="Dashboard{T}.Query"/> with one table
/// (concept §4.11, design §10.1). Every part is typed; nothing in a query is parsed. The rows asked about
/// are the dataset narrowed by <see cref="Selections"/>, with every selection applied and no own-facet
/// exclusion, since a query counts no facet. They are grouped by up to two <see cref="GroupBy"/>
/// dimensions and summarised by <see cref="Values"/>, then ordered and cut.
/// </summary>
/// <typeparam name="T">The row type of the dashboard the query is asked of.</typeparam>
public sealed record DashboardQuery<T>
{
    /// <summary>The selections that narrow the rows, under the rules of concept §4.1. Empty by default: every row.</summary>
    public Selections Selections { get; init; } = Selections.Empty;

    /// <summary>Zero, one or two dimensions. With none the answer is one row over all the rows asked about.</summary>
    public IReadOnlyList<Dimension> GroupBy { get; init; } = [];

    /// <summary>What each group reports. Empty means a count per group.</summary>
    public IReadOnlyList<QueryValue> Values { get; init; } = [];

    /// <summary>The column the groups are ordered by. Null orders them by their dimensions: intervals and periods ascending, other values by value, null last.</summary>
    public QueryOrder? OrderBy { get; init; }

    /// <summary>How many groups to return at most. Null returns every group; the answer's <see cref="QueryResult.Total"/> says how many there were.</summary>
    public int? Take { get; init; }
}

/// <summary>
/// A dimension of a query: what its rows are grouped by (design §10.1). The name means a facet when a facet
/// has that key (concept §4.11). A date dimension may name a period.
/// </summary>
public sealed record Dimension
{
    private Dimension(string name, DateGranularity? granularity, string key)
    {
        Name = name;
        Granularity = granularity;
        Key = key;
    }

    /// <summary>The facet key the dimension groups by.</summary>
    public string Name { get; }

    /// <summary>The period a date dimension groups by; null for the facet's own.</summary>
    public DateGranularity? Granularity { get; }

    /// <summary>The key of the answer's column and what <see cref="QueryOrder"/> refers to: the name, or <c>Month(OrderDate)</c> with a period, unless renamed with <see cref="As"/>.</summary>
    public string Key { get; }

    /// <summary>Groups by the facet <paramref name="name"/>, as the facet presents its values, buckets or periods.</summary>
    public static Dimension Of(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new Dimension(name, null, name);
    }

    /// <summary>Groups the date facet <paramref name="name"/> by <paramref name="granularity"/> (concept §4.11).</summary>
    public static Dimension Of(string name, DateGranularity granularity)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!Enum.IsDefined(granularity))
        {
            throw new ArgumentOutOfRangeException(nameof(granularity));
        }

        return new Dimension(name, granularity, $"{granularity}({name})");
    }

    /// <summary>The same dimension under another key.</summary>
    public Dimension As(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new Dimension(Name, Granularity, key);
    }
}

/// <summary>
/// What a query reports per group (design §10.1): a dashboard metric, a count, or an aggregation over a
/// facet's values with the null rules of concept §4.4.
/// </summary>
public sealed record QueryValue
{
    private QueryValue(string? metricKey, Aggregation? aggregation, string? of, string key)
    {
        MetricKey = metricKey;
        Aggregation = aggregation;
        Of = of;
        Key = key;
    }

    /// <summary>The key of the dashboard metric this value reads; null for an aggregation.</summary>
    public string? MetricKey { get; }

    /// <summary>The aggregation this value computes; null when it reads a metric.</summary>
    public Aggregation? Aggregation { get; }

    /// <summary>The name aggregated; null for a metric and for a count.</summary>
    public string? Of { get; }

    /// <summary>The key of the answer's column: the metric's key, <c>Count</c>, or <c>Average(Amount)</c>, unless renamed with <see cref="As"/>.</summary>
    public string Key { get; }

    /// <summary>The dashboard metric <paramref name="key"/> per group, calculated metrics included. No share: a group is not a part of anything.</summary>
    public static QueryValue Metric(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new QueryValue(key, null, null, key);
    }

    /// <summary>The number of rows per group.</summary>
    public static QueryValue Count() => new(null, Linq2Dashboard.Aggregation.Count, null, "Count");

    /// <summary>The sum of a numeric facet's values per group, skipping null.</summary>
    public static QueryValue Sum(string name) => Over(Linq2Dashboard.Aggregation.Sum, name);

    /// <summary>The average of a numeric facet's values per group, over the rows that have one.</summary>
    public static QueryValue Average(string name) => Over(Linq2Dashboard.Aggregation.Average, name);

    /// <summary>The smallest of a numeric facet's values per group.</summary>
    public static QueryValue Min(string name) => Over(Linq2Dashboard.Aggregation.Min, name);

    /// <summary>The largest of a numeric facet's values per group.</summary>
    public static QueryValue Max(string name) => Over(Linq2Dashboard.Aggregation.Max, name);

    /// <summary>The number of different non-null values of a facet per group, with the facet's equality.</summary>
    public static QueryValue Distinct(string name) => Over(Linq2Dashboard.Aggregation.Distinct, name);

    /// <summary>The same value under another key.</summary>
    public QueryValue As(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new QueryValue(MetricKey, Aggregation, Of, key);
    }

    private static QueryValue Over(Aggregation aggregation, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new QueryValue(null, aggregation, name, $"{aggregation}({name})");
    }
}

/// <summary>The order of a query's groups: one column by key, in either direction. Null sorts last in both (concept §4.11).</summary>
public sealed record QueryOrder
{
    private QueryOrder(string key, bool descending)
    {
        Key = key;
        IsDescending = descending;
    }

    /// <summary>The key of the dimension or value to order by.</summary>
    public string Key { get; }

    /// <summary>True for largest first.</summary>
    public bool IsDescending { get; }

    /// <summary>Smallest first.</summary>
    public static QueryOrder Ascending(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new QueryOrder(key, false);
    }

    /// <summary>Largest first.</summary>
    public static QueryOrder Descending(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new QueryOrder(key, true);
    }
}
