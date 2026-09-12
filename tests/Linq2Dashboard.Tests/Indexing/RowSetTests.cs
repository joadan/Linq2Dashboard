using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Tests.Indexing;

public class RowSetTests
{
    /// <summary>Lengths chosen to hit empty, single word, exact word boundaries, and vector boundaries.</summary>
    public static TheoryData<int> Lengths => [0, 1, 63, 64, 65, 127, 128, 129, 255, 256, 257, 1000, 4097];

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Empty_has_no_rows(int length)
    {
        var set = RowSet.Empty(length);

        Assert.Equal(length, set.Length);
        Assert.Equal(0, set.Count);
        Assert.True(set.IsEmpty);
        Assert.Equal(length == 0, set.IsFull);
        Assert.Empty(set.Rows());
        for (int row = 0; row < length; row++)
        {
            Assert.False(set.Contains(row));
        }
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Full_has_every_row_and_nothing_beyond(int length)
    {
        var set = RowSet.Full(length);

        Assert.Equal(length, set.Length);
        Assert.Equal(length, set.Count);
        Assert.True(set.IsFull);
        Assert.Equal(Enumerable.Range(0, length), set.Rows());
        AssertNoBitsBeyondLength(set);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Builder_sets_rows_and_build_matches(int length)
    {
        int[] rows = SampleRows(length, seed: 1);
        var builder = new RowSetBuilder(length);
        foreach (int row in rows)
        {
            builder.Set(row);
            builder.Set(row); // idempotent
        }

        foreach (int row in rows)
        {
            Assert.True(builder.Contains(row));
        }

        var set = builder.Build();

        Assert.Equal(rows.Length, set.Count);
        Assert.Equal(rows, set.Rows());
        AssertNoBitsBeyondLength(set);
    }

    [Fact]
    public void Builder_clear_removes_a_row()
    {
        var builder = new RowSetBuilder(100);
        builder.Set(5);
        builder.Set(70);
        builder.Clear(5);

        var set = builder.Build();

        Assert.Equal([70], set.Rows());
    }

    [Fact]
    public void Builder_cannot_be_used_after_build()
    {
        var builder = new RowSetBuilder(10);
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Set(1));
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Builder_rejects_rows_outside_range()
    {
        var builder = new RowSetBuilder(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Set(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Set(10));
    }

    [Fact]
    public void Contains_rejects_rows_outside_range()
    {
        var set = RowSet.Full(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => set.Contains(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.Contains(10));
    }

    [Fact]
    public void Negative_length_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RowSet.Empty(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RowSet.Full(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RowSetBuilder(-1));
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void And_matches_brute_force(int length)
    {
        int[] a = SampleRows(length, seed: 2);
        int[] b = SampleRows(length, seed: 3);
        var expected = a.Intersect(b).Order().ToArray();

        var result = RowSet.FromRows(length, a).And(RowSet.FromRows(length, b));

        Assert.Equal(expected.Length, result.Count);
        Assert.Equal(expected, result.Rows());
        AssertNoBitsBeyondLength(result);
    }

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Or_matches_brute_force(int length)
    {
        int[] a = SampleRows(length, seed: 4);
        int[] b = SampleRows(length, seed: 5);
        var expected = a.Union(b).Order().ToArray();

        var result = RowSet.FromRows(length, a).Or(RowSet.FromRows(length, b));

        Assert.Equal(expected.Length, result.Count);
        Assert.Equal(expected, result.Rows());
        AssertNoBitsBeyondLength(result);
    }

    [Fact]
    public void And_with_full_or_empty_takes_shortcuts()
    {
        var some = RowSet.FromRows(200, [1, 64, 199]);
        var full = RowSet.Full(200);
        var empty = RowSet.Empty(200);

        Assert.Same(some, some.And(full));
        Assert.Same(some, full.And(some));
        Assert.Same(empty, some.And(empty));
        Assert.Same(empty, empty.And(some));
    }

    [Fact]
    public void Or_with_full_or_empty_takes_shortcuts()
    {
        var some = RowSet.FromRows(200, [1, 64, 199]);
        var full = RowSet.Full(200);
        var empty = RowSet.Empty(200);

        Assert.Same(full, some.Or(full));
        Assert.Same(full, full.Or(some));
        Assert.Same(some, some.Or(empty));
        Assert.Same(some, empty.Or(some));
    }

    [Fact]
    public void Operations_between_different_lengths_throw()
    {
        var a = RowSet.Full(10);
        var b = RowSet.Full(11);

        Assert.Throws<ArgumentException>(() => a.And(b));
        Assert.Throws<ArgumentException>(() => a.Or(b));
    }

    [Fact]
    public void Enumeration_crosses_word_boundaries_in_order()
    {
        int[] rows = [0, 1, 63, 64, 65, 127, 128, 191, 192, 255];
        var set = RowSet.FromRows(256, rows);

        var seen = new List<int>();
        foreach (int row in set)
        {
            seen.Add(row);
        }

        Assert.Equal(rows, seen);
    }

    [Fact]
    public void Enumeration_of_empty_words_in_the_middle_skips_them()
    {
        var set = RowSet.FromRows(1000, [3, 900]);

        Assert.Equal([3, 900], set.Rows());
    }

    [Fact]
    public void Equality_is_by_content()
    {
        var a = RowSet.FromRows(130, [1, 70, 129]);
        var b = RowSet.FromRows(130, [129, 70, 1]);
        var c = RowSet.FromRows(130, [1, 70]);
        var d = RowSet.FromRows(131, [1, 70, 129]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void FromOwnedWords_counts_the_bits()
    {
        ulong[] words = [0b1011UL, 1UL << 5];

        var set = RowSet.FromOwnedWords(words, 128);

        Assert.Equal(4, set.Count);
        Assert.Equal([0, 1, 3, 69], set.Rows());
    }

    private static int[] SampleRows(int length, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, length)
            .Where(_ => random.Next(3) == 0)
            .ToArray();
    }

    private static void AssertNoBitsBeyondLength(RowSet set)
    {
        int remainder = set.Length & 63;
        if (remainder == 0 || set.Words.Length == 0)
        {
            return;
        }

        ulong lastWord = set.Words[^1];
        ulong beyond = lastWord & ~(ulong.MaxValue >> (64 - remainder));
        Assert.Equal(0UL, beyond);
    }
}
