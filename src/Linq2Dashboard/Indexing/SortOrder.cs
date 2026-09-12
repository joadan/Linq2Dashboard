namespace Linq2Dashboard.Indexing;

/// <summary>One key of the application-defined result order (design §3.3, concept §8).</summary>
internal abstract class SortKey<T>
{
    /// <summary>Reads every row's key once and returns a comparison over row ids.</summary>
    public abstract Comparison<int> Materialize(T[] items);
}

internal sealed class SortKey<T, TKey> : SortKey<T>
{
    private readonly Func<T, TKey> _selector;
    private readonly bool _descending;
    private readonly IComparer<TKey> _comparer;

    public SortKey(Func<T, TKey> selector, bool descending, IComparer<TKey>? comparer = null)
    {
        _selector = selector;
        _descending = descending;
        _comparer = comparer ?? Comparer<TKey>.Default;
    }

    public override Comparison<int> Materialize(T[] items)
    {
        var keys = new TKey[items.Length];
        for (int row = 0; row < items.Length; row++)
        {
            keys[row] = _selector(items[row]);
        }

        IComparer<TKey> comparer = _comparer;
        return _descending
            ? (a, b) => comparer.Compare(keys[b], keys[a])
            : (a, b) => comparer.Compare(keys[a], keys[b]);
    }
}

internal static class SortOrder
{
    /// <summary>
    /// Row ids in the application-defined order, or null when no order is defined so the identity
    /// order can be used without allocating. Ties fall back to row id, which makes the sort stable
    /// and deterministic.
    /// </summary>
    public static int[]? Build<T>(T[] items, IReadOnlyList<SortKey<T>> keys)
    {
        if (keys.Count == 0)
        {
            return null;
        }

        var comparisons = new Comparison<int>[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            comparisons[i] = keys[i].Materialize(items);
        }

        var rows = new int[items.Length];
        for (int row = 0; row < rows.Length; row++)
        {
            rows[row] = row;
        }

        Array.Sort(rows, (a, b) =>
        {
            foreach (Comparison<int> compare in comparisons)
            {
                int result = compare(a, b);
                if (result != 0)
                {
                    return result;
                }
            }

            return a.CompareTo(b);
        });

        return rows;
    }
}
