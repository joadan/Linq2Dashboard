using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Metrics;

/// <summary>
/// A metric as configured by the builder. Count has no selector; sum, average, min and max read a
/// nullable double; distinct encodes its values into a <see cref="DistinctColumn"/>.
/// </summary>
internal sealed class MetricDefinition<T>
{
    private readonly Func<T, double?>? selector;
    private readonly Func<T[], DistinctColumn>? buildDistinct;

    /// <summary>A count (no selector) or a numeric aggregation over <paramref name="selector"/>.</summary>
    public MetricDefinition(string key, Aggregation aggregation, Func<T, double?>? selector)
    {
        Key = key;
        Title = key;
        Aggregation = aggregation;
        this.selector = selector;
    }

    /// <summary>A distinct count over the column <paramref name="buildDistinct"/> encodes at build.</summary>
    public MetricDefinition(string key, Func<T[], DistinctColumn> buildDistinct)
    {
        Key = key;
        Title = key;
        Aggregation = Aggregation.Distinct;
        this.buildDistinct = buildDistinct;
    }

    public string Key { get; }

    public string Title { get; set; }

    public Aggregation Aggregation { get; }

    public MetricInfo Info => new(Key, Title, Aggregation);

    public MetricIndex Build(T[] items)
    {
        if (buildDistinct is not null)
        {
            return new MetricIndex(Key, Title, Aggregation, null, buildDistinct(items), items.Length);
        }

        if (selector is null)
        {
            return new MetricIndex(Key, Title, Aggregation, null, null, items.Length);
        }

        var column = MetricColumn.Build(items.Length, (int row, out double value) =>
        {
            double? read = selector(items[row]);
            value = read.GetValueOrDefault();
            return read.HasValue;
        });

        return new MetricIndex(Key, Title, Aggregation, column, null, items.Length);
    }
}

/// <summary>Built metric: its aggregation and, unless it is a count, the column it reads.</summary>
internal sealed class MetricIndex
{
    public MetricIndex(string key, string title, Aggregation aggregation, MetricColumn? column, DistinctColumn? distinct, int rowCount)
    {
        Key = key;
        Title = title;
        Aggregation = aggregation;
        Column = column;
        Distinct = distinct;
        RowCount = rowCount;
    }

    public string Key { get; }

    public string Title { get; }

    public Aggregation Aggregation { get; }

    /// <summary>The numeric column of sum, average, min and max; null for count and distinct.</summary>
    public MetricColumn? Column { get; }

    /// <summary>The code column of a distinct count; null for every other aggregation.</summary>
    public DistinctColumn? Distinct { get; }

    public int RowCount { get; }

    public MetricInfo Info => new(Key, Title, Aggregation);

    /// <summary>
    /// The metric's state over <paramref name="matching"/>: its value, null when no row contributed,
    /// and for count, sum and distinct the value's share of the total over every row after fixed
    /// filters (concept §4.4, design §4.6).
    /// </summary>
    public MetricState Present(RowSet matching)
    {
        ArgumentNullException.ThrowIfNull(matching);
        if (Distinct is not null)
        {
            int distinct = Distinct.CountIn(matching);
            return distinct == 0
                ? new MetricState(Key, Title, Aggregation, null, null)
                : new MetricState(Key, Title, Aggregation, distinct, ShareOf(distinct, Distinct.DistinctCount));
        }

        if (Column is null)
        {
            return new MetricState(Key, Title, Aggregation, matching.Count, ShareOf(matching.Count, RowCount));
        }

        MetricAggregate aggregate = Column.Aggregate(matching);
        return Aggregation switch
        {
            Aggregation.Sum => new MetricState(Key, Title, Aggregation, aggregate.SumOrNull, aggregate.IsEmpty ? null : ShareOf(aggregate.Sum, Column.Total.Sum)),
            Aggregation.Average => new MetricState(Key, Title, Aggregation, aggregate.Average, null),
            Aggregation.Min => new MetricState(Key, Title, Aggregation, aggregate.MinOrNull, null),
            Aggregation.Max => new MetricState(Key, Title, Aggregation, aggregate.MaxOrNull, null),
            _ => throw new InvalidOperationException($"Unexpected aggregation {Aggregation}."),
        };
    }

    /// <summary>A share of a zero total is undefined: there is nothing to be a part of.</summary>
    private static double? ShareOf(double value, double total) => total == 0 ? null : value / total;
}
