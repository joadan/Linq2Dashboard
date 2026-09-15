using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Metrics;

/// <summary>
/// A metric as configured by the builder. Count has no selector; sum, average, min and max read a
/// nullable double; distinct encodes its values into a <see cref="DistinctColumn"/>; calculated
/// holds a formula over the metrics defined before it.
/// </summary>
internal sealed class MetricDefinition<T>
{
    private readonly Func<T, double?>? selector;
    private readonly Func<T[], DistinctColumn>? buildDistinct;
    private readonly Func<MetricValues, double?>? formula;

    /// <summary>A count (no selector) or a numeric aggregation over <paramref name="selector"/>.</summary>
    public MetricDefinition(string key, Aggregation aggregation, Func<T, double?>? selector)
    {
        Key = key;
        Name = key;
        Aggregation = aggregation;
        this.selector = selector;
    }

    /// <summary>A distinct count over the column <paramref name="buildDistinct"/> encodes at build.</summary>
    public MetricDefinition(string key, Func<T[], DistinctColumn> buildDistinct)
    {
        Key = key;
        Name = key;
        Aggregation = Aggregation.Distinct;
        this.buildDistinct = buildDistinct;
    }

    /// <summary>A calculated metric: <paramref name="formula"/> over the values of earlier metrics.</summary>
    public MetricDefinition(string key, Func<MetricValues, double?> formula)
    {
        Key = key;
        Name = key;
        Aggregation = Aggregation.Calculated;
        this.formula = formula;
    }

    public string Key { get; }

    public string Name { get; set; }

    public Aggregation Aggregation { get; }

    public MetricInfo Info => new(Key, Name, Aggregation);

    public MetricIndex Build(T[] items)
    {
        if (formula is not null)
        {
            return new MetricIndex(Key, Name, Aggregation, null, null, formula, items.Length);
        }

        if (buildDistinct is not null)
        {
            return new MetricIndex(Key, Name, Aggregation, null, buildDistinct(items), null, items.Length);
        }

        if (selector is null)
        {
            return new MetricIndex(Key, Name, Aggregation, null, null, null, items.Length);
        }

        var column = MetricColumn.Build(items.Length, (int row, out double value) =>
        {
            double? read = selector(items[row]);
            value = read.GetValueOrDefault();
            return read.HasValue;
        });

        return new MetricIndex(Key, Name, Aggregation, column, null, null, items.Length);
    }
}

/// <summary>Built metric: its aggregation and, depending on it, the column it reads or the formula it applies.</summary>
internal sealed class MetricIndex
{
    private readonly Func<MetricValues, double?>? formula;
    private readonly MetricAggregate total;
    private readonly int distinctTotal;

    public MetricIndex(string key, string name, Aggregation aggregation, MetricColumn? column, DistinctColumn? distinct, Func<MetricValues, double?>? formula, int rowCount)
        : this(key, name, aggregation, column, distinct, formula, rowCount, column?.Total ?? MetricAggregate.Empty, distinct?.DistinctCount ?? 0)
    {
    }

    private MetricIndex(string key, string name, Aggregation aggregation, MetricColumn? column, DistinctColumn? distinct, Func<MetricValues, double?>? formula, int rowCount, MetricAggregate total, int distinctTotal)
    {
        Key = key;
        Name = name;
        Aggregation = aggregation;
        Column = column;
        Distinct = distinct;
        this.formula = formula;
        RowCount = rowCount;
        this.total = total;
        this.distinctTotal = distinctTotal;
    }

    public string Key { get; }

    public string Name { get; }

    public Aggregation Aggregation { get; }

    /// <summary>The numeric column of sum, average, min and max; null for the other aggregations.</summary>
    public MetricColumn? Column { get; }

    /// <summary>The code column of a distinct count; null for every other aggregation.</summary>
    public DistinctColumn? Distinct { get; }

    /// <summary>Rows the share is measured against: the dataset, or the scope for an index made by <see cref="Scope"/> (concept §4.10).</summary>
    public int RowCount { get; }

    public MetricInfo Info => new(Key, Name, Aggregation);

    /// <summary>
    /// This metric with its totals over the rows in <paramref name="scope"/> (concept §4.10): one
    /// aggregation pass for a numeric column, one distinct count for a distinct column, nothing else.
    /// </summary>
    public MetricIndex Scope(RowSet scope) => new(
        Key, Name, Aggregation, Column, Distinct, formula, scope.Count,
        Column?.Aggregate(scope) ?? MetricAggregate.Empty,
        Distinct?.CountIn(scope) ?? 0);

    /// <summary>
    /// The metric's state over <paramref name="matching"/>: its value, null when no row contributed,
    /// and for count, sum and distinct the value's share of the total over every row after fixed
    /// filters (concept §4.4, design §4.6). A calculated metric reads <paramref name="earlier"/>,
    /// the states of the metrics defined before it, and has no share.
    /// </summary>
    public MetricState Present(RowSet matching, MetricValues earlier)
    {
        ArgumentNullException.ThrowIfNull(matching);
        if (formula is not null)
        {
            double? result = formula(earlier);
            return new MetricState(Key, Name, Aggregation, result is double r && double.IsFinite(r) ? r : null, null);
        }

        if (Distinct is not null)
        {
            int distinct = Distinct.CountIn(matching);
            return distinct == 0
                ? new MetricState(Key, Name, Aggregation, null, null)
                : new MetricState(Key, Name, Aggregation, distinct, ShareOf(distinct, distinctTotal));
        }

        if (Column is null)
        {
            return new MetricState(Key, Name, Aggregation, matching.Count, ShareOf(matching.Count, RowCount));
        }

        MetricAggregate aggregate = Column.Aggregate(matching);
        return Aggregation switch
        {
            Aggregation.Sum => new MetricState(Key, Name, Aggregation, aggregate.SumOrNull, aggregate.IsEmpty ? null : ShareOf(aggregate.Sum, total.Sum)),
            Aggregation.Average => new MetricState(Key, Name, Aggregation, aggregate.Average, null),
            Aggregation.Min => new MetricState(Key, Name, Aggregation, aggregate.MinOrNull, null),
            Aggregation.Max => new MetricState(Key, Name, Aggregation, aggregate.MaxOrNull, null),
            _ => throw new InvalidOperationException($"Unexpected aggregation {Aggregation}."),
        };
    }

    /// <summary>A share of a zero total is undefined: there is nothing to be a part of.</summary>
    private static double? ShareOf(double value, double total) => total == 0 ? null : value / total;
}
