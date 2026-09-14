using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Metrics;

/// <summary>A metric as configured by the builder. Count has no selector; the others read a nullable double.</summary>
internal sealed class MetricDefinition<T>
{
    private readonly Func<T, double?>? selector;

    public MetricDefinition(string key, Aggregation aggregation, Func<T, double?>? selector)
    {
        Key = key;
        Title = key;
        Aggregation = aggregation;
        this.selector = selector;
    }

    public string Key { get; }

    public string Title { get; set; }

    public Aggregation Aggregation { get; }

    public MetricInfo Info => new(Key, Title, Aggregation);

    public MetricIndex Build(T[] items)
    {
        if (selector is null)
        {
            return new MetricIndex(Key, Title, Aggregation, null, items.Length);
        }

        var column = MetricColumn.Build(items.Length, (int row, out double value) =>
        {
            double? read = selector(items[row]);
            value = read.GetValueOrDefault();
            return read.HasValue;
        });

        return new MetricIndex(Key, Title, Aggregation, column, items.Length);
    }
}

/// <summary>Built metric: its aggregation and, unless it is a count, its column.</summary>
internal sealed class MetricIndex
{
    public MetricIndex(string key, string title, Aggregation aggregation, MetricColumn? column, int rowCount)
    {
        Key = key;
        Title = title;
        Aggregation = aggregation;
        Column = column;
        RowCount = rowCount;
    }

    public string Key { get; }

    public string Title { get; }

    public Aggregation Aggregation { get; }

    /// <summary>Null for <see cref="Linq2Dashboard.Aggregation.Count"/>.</summary>
    public MetricColumn? Column { get; }

    public int RowCount { get; }

    public MetricInfo Info => new(Key, Title, Aggregation);

    /// <summary>
    /// The metric's state over <paramref name="matching"/>: its value, null when no row contributed,
    /// and for count and sum the value's share of the total over every row after fixed filters
    /// (concept §4.4, design §4.6).
    /// </summary>
    public MetricState Present(RowSet matching)
    {
        ArgumentNullException.ThrowIfNull(matching);
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
