using System.Numerics;
using System.Runtime.InteropServices;

namespace Linq2Dashboard.Indexing;

/// <summary>
/// An immutable set of row ids in the range <c>[0, Length)</c>, stored as a fixed-length bitset.
/// </summary>
/// <remarks>
/// Invariant: every bit at or beyond <see cref="Length"/> is zero. All factory methods and
/// operations preserve this, which is what makes <see cref="Count"/> a plain popcount and lets
/// <see cref="And"/> and <see cref="Or"/> operate on whole words without a final mask.
/// Operations between sets of different lengths are a programming error and throw.
/// </remarks>
internal sealed class RowSet : IEquatable<RowSet>
{
    private readonly ulong[] words;

    private RowSet(ulong[] words, int length, int count)
    {
        this.words = words;
        Length = length;
        Count = count;
    }

    /// <summary>Number of rows the set is defined over. Fixed for the lifetime of the dashboard.</summary>
    public int Length { get; }

    /// <summary>Number of rows in the set.</summary>
    public int Count { get; }

    public bool IsEmpty => Count == 0;

    public bool IsFull => Count == Length;

    /// <summary>The raw words. Exposed for tests and for tightly coupled indexing code only.</summary>
    internal ReadOnlySpan<ulong> Words => words;

    internal static int WordCount(int length) => (length + 63) >> 6;

    public static RowSet Empty(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new RowSet(new ulong[WordCount(length)], length, 0);
    }

    public static RowSet Full(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var words = new ulong[WordCount(length)];
        if (length == 0)
        {
            return new RowSet(words, 0, 0);
        }

        Array.Fill(words, ulong.MaxValue);
        int remainder = length & 63;
        if (remainder != 0)
        {
            words[^1] = ulong.MaxValue >> (64 - remainder);
        }

        return new RowSet(words, length, length);
    }

    /// <summary>Convenience for tests and small sets. Hot paths use <see cref="RowSetBuilder"/>.</summary>
    public static RowSet FromRows(int length, IEnumerable<int> rows)
    {
        var builder = new RowSetBuilder(length);
        foreach (int row in rows)
        {
            builder.Set(row);
        }

        return builder.Build();
    }

    /// <summary>
    /// Wraps an array whose bits beyond <paramref name="length"/> are already zero.
    /// The caller gives up ownership of <paramref name="words"/>.
    /// </summary>
    internal static RowSet FromOwnedWords(ulong[] words, int length)
    {
        return new RowSet(words, length, PopCount(words));
    }

    public bool Contains(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Length);
        return (words[row >> 6] & (1UL << (row & 63))) != 0;
    }

    public RowSet And(RowSet other)
    {
        ThrowIfLengthDiffers(other);

        if (IsEmpty || other.IsFull)
        {
            return this;
        }

        if (other.IsEmpty || IsFull)
        {
            return other;
        }

        var result = new ulong[words.Length];
        AndWords(words, other.words, result);
        return FromOwnedWords(result, Length);
    }

    public RowSet Or(RowSet other)
    {
        ThrowIfLengthDiffers(other);

        if (IsFull || other.IsEmpty)
        {
            return this;
        }

        if (other.IsFull || IsEmpty)
        {
            return other;
        }

        var result = new ulong[words.Length];
        OrWords(words, other.words, result);
        return FromOwnedWords(result, Length);
    }

    /// <summary>Allocation-free enumeration of rows in ascending order: <c>foreach (int row in set)</c>.</summary>
    public Enumerator GetEnumerator() => new(words);

    /// <summary>Rows in ascending order as a sequence, for LINQ and tests.</summary>
    public IEnumerable<int> Rows()
    {
        foreach (int row in this)
        {
            yield return row;
        }
    }

    public bool Equals(RowSet? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Length == other.Length
            && Count == other.Count
            && words.AsSpan().SequenceEqual(other.words);
    }

    public override bool Equals(object? obj) => Equals(obj as RowSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Length);
        hash.Add(Count);
        hash.AddBytes(MemoryMarshal.AsBytes(words.AsSpan()));
        return hash.ToHashCode();
    }

    public override string ToString() => $"RowSet({Count} of {Length})";

    private void ThrowIfLengthDiffers(RowSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Length != Length)
        {
            throw new ArgumentException(
                $"Row sets have different lengths ({Length} and {other.Length}).", nameof(other));
        }
    }

    private static int PopCount(ReadOnlySpan<ulong> words)
    {
        int count = 0;
        foreach (ulong word in words)
        {
            count += BitOperations.PopCount(word);
        }

        return count;
    }

    private static void AndWords(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b, Span<ulong> result)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<ulong>.Count)
        {
            ref ulong ra = ref MemoryMarshal.GetReference(a);
            ref ulong rb = ref MemoryMarshal.GetReference(b);
            ref ulong rr = ref MemoryMarshal.GetReference(result);
            int last = a.Length - Vector<ulong>.Count;
            for (; i <= last; i += Vector<ulong>.Count)
            {
                (Vector.LoadUnsafe(ref ra, (nuint)i) & Vector.LoadUnsafe(ref rb, (nuint)i))
                    .StoreUnsafe(ref rr, (nuint)i);
            }
        }

        for (; i < a.Length; i++)
        {
            result[i] = a[i] & b[i];
        }
    }

    private static void OrWords(ReadOnlySpan<ulong> a, ReadOnlySpan<ulong> b, Span<ulong> result)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<ulong>.Count)
        {
            ref ulong ra = ref MemoryMarshal.GetReference(a);
            ref ulong rb = ref MemoryMarshal.GetReference(b);
            ref ulong rr = ref MemoryMarshal.GetReference(result);
            int last = a.Length - Vector<ulong>.Count;
            for (; i <= last; i += Vector<ulong>.Count)
            {
                (Vector.LoadUnsafe(ref ra, (nuint)i) | Vector.LoadUnsafe(ref rb, (nuint)i))
                    .StoreUnsafe(ref rr, (nuint)i);
            }
        }

        for (; i < a.Length; i++)
        {
            result[i] = a[i] | b[i];
        }
    }

    /// <summary>Walks set bits in ascending row order using trailing-zero counts.</summary>
    public struct Enumerator
    {
        private readonly ulong[] words;
        private int wordIndex;
        private ulong remaining;

        internal Enumerator(ulong[] words)
        {
            this.words = words;
            wordIndex = -1;
            remaining = 0;
            Current = -1;
        }

        public int Current { get; private set; }

        public bool MoveNext()
        {
            while (remaining == 0)
            {
                if (++wordIndex >= words.Length)
                {
                    return false;
                }

                remaining = words[wordIndex];
            }

            int bit = BitOperations.TrailingZeroCount(remaining);
            remaining &= remaining - 1;
            Current = (wordIndex << 6) + bit;
            return true;
        }
    }
}
