namespace Linq2Dashboard.Indexing;

/// <summary>
/// Mutable accumulator for a <see cref="RowSet"/>. Used by column scans that decide row by row
/// whether a row belongs to a set. <see cref="Build"/> hands the bits over without copying, after
/// which the builder must not be used again.
/// </summary>
internal sealed class RowSetBuilder
{
    private ulong[]? words;

    public RowSetBuilder(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
        words = new ulong[RowSet.WordCount(length)];
    }

    public int Length { get; }

    public void Set(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Length);
        Words[row >> 6] |= 1UL << (row & 63);
    }

    public void Clear(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Length);
        Words[row >> 6] &= ~(1UL << (row & 63));
    }

    public bool Contains(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Length);
        return (Words[row >> 6] & (1UL << (row & 63))) != 0;
    }

    /// <summary>Produces the set and invalidates the builder.</summary>
    public RowSet Build()
    {
        ulong[] words = Words;
        this.words = null;
        return RowSet.FromOwnedWords(words, Length);
    }

    private ulong[] Words => words ?? throw new InvalidOperationException(
        "The builder has already been built and can no longer be used.");
}
