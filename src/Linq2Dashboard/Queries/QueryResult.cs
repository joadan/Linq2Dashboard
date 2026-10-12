namespace Linq2Dashboard;

/// <summary>
/// The answer to a <see cref="DashboardQuery{T}"/> (design §10.2): a table whose columns say what they
/// are, rows in the query's order, and how many rows there were before the query's take cut them.
/// Immutable.
/// </summary>
public sealed class QueryResult
{
    private readonly Dictionary<string, int> positions;

    internal QueryResult(QueryColumn[] columns, object?[][] cells, int total)
    {
        Columns = columns;
        positions = columns.Select((c, i) => (c.Key, i)).ToDictionary(p => p.Key, p => p.i, StringComparer.Ordinal);
        Rows = cells.Select(row => new QueryRow(row, positions)).ToArray();
        Total = total;
    }

    /// <summary>Dimensions first, then values, in the query's order.</summary>
    public IReadOnlyList<QueryColumn> Columns { get; }

    /// <summary>The rows of the answer, in the query's order.</summary>
    public IReadOnlyList<QueryRow> Rows { get; }

    /// <summary>The number of groups, or of matching rows for a row query, before the query's take (concept §4.11).</summary>
    public int Total { get; }

    /// <summary>True when the take left groups or rows out.</summary>
    public bool IsCut => Rows.Count < Total;

    /// <summary>The column with <paramref name="key"/>. Throws for a key the answer does not have.</summary>
    public QueryColumn Column(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return positions.TryGetValue(key, out int position)
            ? Columns[position]
            : throw new ArgumentException($"The answer has no column '{key}'.", nameof(key));
    }
}

/// <summary>
/// One column of a <see cref="QueryResult"/> (design §10.2): its key, its display name, whether it is a
/// dimension or a value, and the kind of cell it holds.
/// </summary>
public sealed record QueryColumn(string Key, string Name, QueryColumnRole Role, QueryColumnKind Kind)
{
    /// <summary>The facet key or metric key the column reads, or the name an aggregation is over; null for a count.</summary>
    public string? Source { get; init; }

    /// <summary>The aggregation of a value column: the metric's own for a metric, the requested one otherwise. Null for a dimension.</summary>
    public Aggregation? Aggregation { get; init; }

    /// <summary>The period of a period column; null for every other kind.</summary>
    public DateGranularity? Granularity { get; init; }
}

/// <summary>Whether a query column groups or reports.</summary>
public enum QueryColumnRole
{
    /// <summary>A dimension: what the rows are grouped by.</summary>
    Dimension,

    /// <summary>A value: what each group reports.</summary>
    Value,
}

/// <summary>What a query column's cells hold (design §10.2).</summary>
public enum QueryColumnKind
{
    /// <summary>A <see cref="QueryValueCell"/>: a facet value with its label, or a property's value. A multi-valued column in a row query holds a list of them.</summary>
    Value,

    /// <summary>A <see cref="RangeInterval"/>: a range facet's bucket.</summary>
    Interval,

    /// <summary>A <see cref="QueryPeriod"/>: a date period.</summary>
    Period,

    /// <summary>A <see cref="DateTimeOffset"/>: a point in time, in a date facet's zone or in UTC for a property.</summary>
    Instant,

    /// <summary>A <see cref="double"/>, or null for "no value" (concept §4.4): a value column, or a range facet's number in a row query.</summary>
    Number,
}

/// <summary>One row of a <see cref="QueryResult"/>. A cell is null for a null group and for "no value".</summary>
public sealed class QueryRow
{
    private readonly object?[] cells;
    private readonly Dictionary<string, int> positions;

    internal QueryRow(object?[] cells, Dictionary<string, int> positions)
    {
        this.cells = cells;
        this.positions = positions;
    }

    /// <summary>The cell in the column at <paramref name="column"/>.</summary>
    public object? this[int column] => cells[column];

    /// <summary>The cell in the column with <paramref name="key"/>. Throws for a key the answer does not have.</summary>
    public object? this[string key] => positions.TryGetValue(key, out int position)
        ? cells[position]
        : throw new ArgumentException($"The answer has no column '{key}'.", nameof(key));

    /// <summary>The cells in column order.</summary>
    public IReadOnlyList<object?> Cells => cells;
}

/// <summary>
/// A facet value in a query's answer (design §10.2): the value, boxed in the facet's own type, and the
/// facet's label for it when the facet has labels (concept §5). <c>selections.Toggle(key, cell.Value)</c>
/// selects it on the dashboard.
/// </summary>
public sealed record QueryValueCell(object? Value, string? Label = null);

/// <summary>
/// A date period in a query's answer (design §10.2): the instants it covers, and its start as a local
/// date-time in the facet's zone for labels, as a <see cref="DateBucket"/> carries them.
/// <c>selections.ToggleInterval(key, cell.Interval)</c> selects it on the dashboard.
/// </summary>
public sealed record QueryPeriod(DateInterval Interval, DateTime PeriodStart, DateGranularity Granularity);
