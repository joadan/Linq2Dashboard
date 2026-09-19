using System.Collections;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.State;

/// <summary>
/// The matching rows of one state as a read-only list in the application-defined order (design §4.7).
/// The ordered row indexes are materialised on first use, four bytes per matching row, and kept for
/// the life of the state; the count comes from the row set and costs nothing. Implements
/// <see cref="IList{T}"/> as well so that LINQ, and through <c>AsQueryable</c> a data grid, takes
/// its list fast paths: <c>Count()</c> reads the count, <c>Skip</c> and <c>Take</c> index straight
/// into the list, and an ordered partition copies through <see cref="CopyTo"/>.
/// </summary>
internal sealed class MatchingItems<T> : IReadOnlyList<T>, IList<T>
{
    private readonly Dashboard<T> dashboard;
    private readonly RowSet matching;
    private int[]? rows;

    internal MatchingItems(Dashboard<T> dashboard, RowSet matching)
    {
        this.dashboard = dashboard;
        this.matching = matching;
    }

    /// <inheritdoc />
    public int Count => matching.Count;

    /// <inheritdoc />
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return dashboard.ItemAt(Rows()[index]);
        }
    }

    /// <summary>Matching rows <paramref name="skip"/> onwards, at most <paramref name="take"/> of them; empty beyond the end.</summary>
    internal IReadOnlyList<T> Slice(long skip, int take)
    {
        if (take == 0 || skip >= Count)
        {
            return [];
        }

        int[] ordered = Rows();
        int start = (int)skip;
        int length = Math.Min(take, Count - start);
        var items = new T[length];
        for (int i = 0; i < length; i++)
        {
            items[i] = dashboard.ItemAt(ordered[start + i]);
        }

        return items;
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator()
    {
        int[] ordered = Rows();
        for (int i = 0; i < ordered.Length; i++)
        {
            yield return dashboard.ItemAt(ordered[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Count, array.Length - arrayIndex);

        int[] ordered = Rows();
        for (int i = 0; i < ordered.Length; i++)
        {
            array[arrayIndex + i] = dashboard.ItemAt(ordered[i]);
        }
    }

    /// <inheritdoc />
    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <inheritdoc />
    public int IndexOf(T item)
    {
        int[] ordered = Rows();
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < ordered.Length; i++)
        {
            if (comparer.Equals(dashboard.ItemAt(ordered[i]), item))
            {
                return i;
            }
        }

        return -1;
    }

    bool ICollection<T>.IsReadOnly => true;

    T IList<T>.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException("The matching rows are read-only.");
    }

    void ICollection<T>.Add(T item) => throw new NotSupportedException("The matching rows are read-only.");

    void ICollection<T>.Clear() => throw new NotSupportedException("The matching rows are read-only.");

    bool ICollection<T>.Remove(T item) => throw new NotSupportedException("The matching rows are read-only.");

    void IList<T>.Insert(int index, T item) => throw new NotSupportedException("The matching rows are read-only.");

    void IList<T>.RemoveAt(int index) => throw new NotSupportedException("The matching rows are read-only.");

    /// <summary>
    /// The matching row indexes in order, built once. Two threads racing here build equal arrays and
    /// the last one wins, which is harmless: the state is immutable and so is the array's content.
    /// </summary>
    private int[] Rows()
    {
        int[]? ordered = Volatile.Read(ref rows);
        if (ordered is not null)
        {
            return ordered;
        }

        ordered = new int[matching.Count];
        int n = 0;
        int[]? sorted = dashboard.SortedRows;
        if (sorted is null)
        {
            foreach (int row in matching)
            {
                ordered[n++] = row;
            }
        }
        else
        {
            foreach (int row in sorted)
            {
                if (matching.Contains(row))
                {
                    ordered[n++] = row;
                }
            }
        }

        Volatile.Write(ref rows, ordered);
        return ordered;
    }
}
