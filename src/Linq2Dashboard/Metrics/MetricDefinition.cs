using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Metrics;

/// <summary>A metric as configured by the builder. Count has no selector; the others read a nullable double.</summary>
internal sealed class MetricDefinition<T>
{
    private readonly Func<T, double?>? _selector;

    public MetricDefinition(string key, Aggregation aggregation, Func<T, double?>? selector)
    {
        Key = key;
        Title = key;
        Aggregation = aggregation;
        _selector = selector;
    }

    public string Key { get; }

    public string Title { get; set; }

    public Aggregation Aggregation { get; }

    public MetricInfo Info => new(Key, Title, Aggregation);

    public MetricIndex Build(T[] items)
    {
        if (_selector is null)
        {
            return new MetricIndex(Key, Title, Aggregation, null, items.Length);
        }

        Func<T, double?> selector = _selector;
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

    /// <summary>The metric's value over <paramref name="matching"/>; null when no row contributed (concept §4.4).</summary>
    public double? Evaluate(RowSet matching)
    {
        ArgumentNullException.ThrowIfNull(matching);
        if (Column is null)
        {
            return matching.Count;
        }

        MetricAggregate aggregate = Column.Aggregate(matching);
        return Aggregation switch
        {
            Aggregation.Sum => aggregate.SumOrNull,
            Aggregation.Average => aggregate.Average,
            Aggregation.Min => aggregate.MinOrNull,
            Aggregation.Max => aggregate.MaxOrNull,
            _ => throw new InvalidOperationException($"Unexpected aggregation {Aggregation}."),
        };
    }
}
