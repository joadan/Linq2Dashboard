using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;
using static Linq2Dashboard.Queries.QueryNames;

namespace Linq2Dashboard.Queries;

/// <summary>
/// One column of a row query (concept §4.11, design §10.2): what a facet or property reads from a row, as a cell,
/// and a key per row to order by.
/// </summary>
internal abstract class RowColumn(QueryColumnKind kind)
{
    public QueryColumnKind Kind { get; } = kind;

    public virtual DateGranularity? Granularity => null;

    /// <summary>False for a column holding several values per row, which has no order.</summary>
    public virtual bool CanOrder => true;

    public abstract object? Cell(int row);

    public abstract RowKey KeyOf(int row);

    /// <summary>Two non-null keys in natural order, ascending.</summary>
    public abstract int Compare(RowKey a, RowKey b);
}

/// <summary>An order key read once per row: a code or ticks in <see cref="Integer"/>, or a number.</summary>
internal readonly record struct RowKey(long Integer, double Number, bool IsNull);

/// <summary>A column read through a group column's codes: a value facet, a period, a plain property by value.</summary>
internal sealed class CodedRowColumn(GroupColumn column) : RowColumn(column.Kind)
{
    public override DateGranularity? Granularity => column.Granularity;

    public override object? Cell(int row) => column.CodeAt(row) is int code and > 0 ? column.Cell(code) : null;

    public override RowKey KeyOf(int row)
    {
        int code = column.CodeAt(row);
        return new RowKey(code, 0, code == 0);
    }

    public override int Compare(RowKey a, RowKey b) => column.Compare((int)a.Integer, (int)b.Integer);
}

/// <summary>A range facet's number for the row (design §10.2).</summary>
internal sealed class NumberRowColumn(double[] values) : RowColumn(QueryColumnKind.Number)
{
    public override object? Cell(int row) => double.IsNaN(values[row]) ? null : values[row];

    public override RowKey KeyOf(int row) => new(0, values[row], double.IsNaN(values[row]));

    public override int Compare(RowKey a, RowKey b) => a.Number.CompareTo(b.Number);
}

/// <summary>A point in time for the row, shown in <paramref name="zone"/>: a date facet's own, or UTC for a property.</summary>
internal sealed class InstantRowColumn(Func<int, long?> utcTicksAt, TimeZoneInfo zone) : RowColumn(QueryColumnKind.Instant)
{
    public override object? Cell(int row) =>
        utcTicksAt(row) is long ticks ? TimeZoneInfo.ConvertTime(new DateTimeOffset(ticks, TimeSpan.Zero), zone) : null;

    public override RowKey KeyOf(int row) => utcTicksAt(row) is long ticks ? new RowKey(ticks, 0, false) : new RowKey(0, 0, true);

    public override int Compare(RowKey a, RowKey b) => a.Integer.CompareTo(b.Integer);
}

/// <summary>A <see cref="DateOnly"/> property for the row, kept a date (design §10.9).</summary>
internal sealed class DateRowColumn(Func<int, DateOnly?> read) : RowColumn(QueryColumnKind.Date)
{
    public override object? Cell(int row) => read(row);

    public override RowKey KeyOf(int row) => read(row) is DateOnly date ? new RowKey(date.DayNumber, 0, false) : new RowKey(0, 0, true);

    public override int Compare(RowKey a, RowKey b) => a.Integer.CompareTo(b.Integer);
}

/// <summary>Several values per row, as a list of cells; a row without any is null (concept §5). Has no order.</summary>
internal sealed class ListRowColumn(Func<int, IReadOnlyList<QueryValueCell>> read) : RowColumn(QueryColumnKind.Value)
{
    public override bool CanOrder => false;

    public override object? Cell(int row) => read(row) is { Count: > 0 } cells ? cells : null;

    public override RowKey KeyOf(int row) => throw new NotSupportedException();

    public override int Compare(RowKey a, RowKey b) => throw new NotSupportedException();
}

/// <summary>
/// Answers a row query (concept §4.11, design §10.3 step 7): the narrowed matching rows, the top N by the order
/// column kept in a heap during one pass, or the first N in the dashboard's own order; columns are read for those
/// rows only.
/// </summary>
internal static class RowQueryRunner
{
    /// <summary>The ceiling on a row query's take: bulk rows come from the state's matching rows (design §10.4).</summary>
    public const int MaxRows = 10_000;

    /// <summary>What a row query returns without a take.</summary>
    public const int DefaultRows = 100;

    public static QueryResult Run<T>(Dashboard<T> dashboard, DashboardQuery<T> query, CancellationToken cancellationToken)
    {
        (RowColumn[] columns, QueryColumn[] described, FacetIndex?[] facets, int? orderColumn, int take) = Resolve(dashboard, query);
        cancellationToken.ThrowIfCancellationRequested();

        RowSet rows = QueryRunner.Rows(dashboard, query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        int[] chosen = orderColumn is int position
            ? Top(rows, columns[position], query.OrderBy!.IsDescending, take, cancellationToken)
            : First(dashboard, rows, take);

        var cells = new object?[chosen.Length][];
        for (int i = 0; i < chosen.Length; i++)
        {
            cells[i] = new object?[columns.Length];
            for (int c = 0; c < columns.Length; c++)
            {
                cells[i][c] = columns[c].Cell(chosen[i]);
            }
        }

        return new QueryResult(described, cells, rows.Count, facets);
    }

    internal static (RowColumn[] Columns, QueryColumn[] Described, FacetIndex?[] Facets, int? OrderColumn, int Take) Resolve<T>(Dashboard<T> dashboard, DashboardQuery<T> query)
    {
        IReadOnlyList<Dimension> requested = query.Columns ?? throw Error("The query's Columns is null.");
        if (query.Selections is null)
        {
            throw Error("The query's Selections is null; use Selections.Empty for every row.");
        }

        if (query.GroupBy is { Count: > 0 } || query.Values is { Count: > 0 })
        {
            throw Error("A row query lists rows, so it has Columns instead of GroupBy and Values.", query.GroupBy is { Count: > 0 } ? "groupBy" : "values");
        }

        int take = query.Take ?? DefaultRows;
        if (take < 0)
        {
            throw Error($"Take must be zero or more; it is {take}.", "take");
        }

        if (take > MaxRows)
        {
            throw Error($"A row query returns at most {MaxRows} rows; this one asks for {take}. Take rows in bulk from the state's Items.", "take");
        }

        CheckSelections(dashboard, query.Selections);

        var columns = new RowColumn[requested.Count];
        var described = new QueryColumn[requested.Count];
        var facets = new FacetIndex?[requested.Count];
        for (int c = 0; c < requested.Count; c++)
        {
            Dimension dimension = requested[c] ?? throw Error("The query's Columns holds a null column.", $"columns[{c}]");
            (FacetIndex? facet, PropertyAccessor<T>? property, RowColumn column) = At($"columns[{c}]", () =>
            {
                (FacetIndex? facet, PropertyAccessor<T>? property) = dimension.IsProperty
                    ? (null, Property(dashboard, dimension.Name))
                    : Name(dashboard, dimension.Name, "list");
                RowColumn column = facet is not null
                    ? facet.RowColumn(dimension.Granularity)
                    : property!.RowColumn(dashboard.ItemArray, dimension.Granularity);
                return (facet, property, column);
            });
            columns[c] = column;
            facets[c] = facet;
            described[c] = new QueryColumn(dimension.Key, facet?.Name ?? property!.Name, QueryColumnRole.Dimension, column.Kind)
            {
                Source = facet?.Key ?? property!.Name,
                Granularity = column.Granularity,
            };
        }

        string? duplicate = described.GroupBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw Error($"Two columns of the query have the key '{duplicate}'. Give one another key with As(...).", $"columns[{Array.FindLastIndex(described, c => c.Key == duplicate)}]");
        }

        int? orderColumn = null;
        if (query.OrderBy is QueryOrder order)
        {
            int position = Array.FindIndex(described, c => c.Key == order.Key);
            if (position < 0)
            {
                throw Error($"The query orders by '{order.Key}', which is not one of its columns: {string.Join(", ", described.Select(c => c.Key))}.", "orderBy");
            }

            if (!columns[position].CanOrder)
            {
                throw Error($"The query orders by '{order.Key}', which holds several values per row and has no order.", "orderBy");
            }

            orderColumn = position;
        }

        return (columns, described, facets, orderColumn, take);
    }

    /// <summary>The first <paramref name="take"/> rows in the dashboard's own order (design §3.3).</summary>
    private static int[] First<T>(Dashboard<T> dashboard, RowSet rows, int take)
    {
        var chosen = new List<int>(Math.Min(take, rows.Count));
        if (dashboard.SortedRows is int[] sorted)
        {
            for (int i = 0; i < sorted.Length && chosen.Count < take; i++)
            {
                if (rows.Contains(sorted[i]))
                {
                    chosen.Add(sorted[i]);
                }
            }
        }
        else
        {
            foreach (int row in rows)
            {
                if (chosen.Count == take)
                {
                    break;
                }

                chosen.Add(row);
            }
        }

        return chosen.ToArray();
    }

    /// <summary>The top <paramref name="take"/> rows by one column, null last in both directions, ties by row.</summary>
    private static int[] Top(RowSet rows, RowColumn column, bool descending, int take, CancellationToken cancellationToken)
    {
        if (take == 0)
        {
            return [];
        }

        int sign = descending ? -1 : 1;
        int Compare((int Row, RowKey Key) a, (int Row, RowKey Key) b)
        {
            int result = (a.Key.IsNull, b.Key.IsNull) switch
            {
                (true, true) => 0,
                (true, false) => 1,
                (false, true) => -1,
                _ => sign * column.Compare(a.Key, b.Key),
            };
            return result != 0 ? result : a.Row.CompareTo(b.Row);
        }

        var worstFirst = Comparer<(int Row, RowKey Key)>.Create((a, b) => Compare(b, a));
        var heap = new PriorityQueue<(int Row, RowKey Key), (int Row, RowKey Key)>(take + 1, worstFirst);
        int visited = 0;
        foreach (int row in rows)
        {
            if (++visited % QueryRunner.CancellationStride == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var entry = (row, column.KeyOf(row));
            if (heap.Count < take)
            {
                heap.Enqueue(entry, entry);
            }
            else if (Compare(entry, heap.Peek()) < 0)
            {
                heap.EnqueueDequeue(entry, entry);
            }
        }

        var kept = new (int Row, RowKey Key)[heap.Count];
        for (int i = 0; i < kept.Length; i++)
        {
            kept[i] = heap.Dequeue();
        }

        Array.Sort(kept, Compare);
        return kept.Select(k => k.Row).ToArray();
    }
}
