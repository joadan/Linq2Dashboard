namespace Linq2Dashboard.Indexing;

/// <summary>Non-generic helpers for <see cref="ValueColumn{TValue}"/>.</summary>
internal static class ValueColumn
{
    /// <summary>
    /// The comparer a value facet uses when the builder does not supply one. Strings compare
    /// ordinal, ignoring case, so "Sweden" and "sweden" are one facet value (design §3.3).
    /// Everything else uses default equality.
    /// </summary>
    public static IEqualityComparer<TValue> DefaultComparer<TValue>() where TValue : notnull
    {
        if (typeof(TValue) == typeof(string))
        {
            return (IEqualityComparer<TValue>)(object)StringComparer.OrdinalIgnoreCase;
        }

        return EqualityComparer<TValue>.Default;
    }
}

/// <summary>
/// Dictionary-encoded column for a value facet (design §3.3). Every row holds one integer code:
/// 0 means null, and codes 1..V index the dictionary of distinct non-null values in first-seen
/// order. Immutable once built.
/// </summary>
internal sealed class ValueColumn<TValue> where TValue : notnull
{
    private readonly int[] _codes;
    private readonly TValue[] _dictionary;
    private readonly Dictionary<TValue, int> _codeOf;
    private readonly int[] _totalCounts;

    private ValueColumn(int[] codes, TValue[] dictionary, Dictionary<TValue, int> codeOf, int[] totalCounts)
    {
        _codes = codes;
        _dictionary = dictionary;
        _codeOf = codeOf;
        _totalCounts = totalCounts;
    }

    /// <summary>Number of rows in the dataset.</summary>
    public int RowCount => _codes.Length;

    /// <summary>Number of distinct non-null values, V. Valid codes are 0..V.</summary>
    public int DistinctCount => _dictionary.Length;

    /// <summary>Rows whose value is null.</summary>
    public int NullCount => _totalCounts[0];

    public bool HasNulls => NullCount > 0;

    /// <summary>One code per row. Sequential reads of this span are the counting hot path.</summary>
    public ReadOnlySpan<int> Codes => _codes;

    /// <summary>Total count per code, index 0 being null. Computed once at build (concept §4.3).</summary>
    public ReadOnlySpan<int> TotalCounts => _totalCounts;

    public int CodeAt(int row) => _codes[row];

    /// <summary>The value behind a non-null code (1..V). The first-seen spelling under the comparer.</summary>
    public TValue ValueOf(int code)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(code, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code, DistinctCount);
        return _dictionary[code - 1];
    }

    public int TotalCount(int code)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(code);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code, DistinctCount);
        return _totalCounts[code];
    }

    /// <summary>Looks up the code of a non-null value. False when the value does not occur in the dataset.</summary>
    public bool TryGetCode(TValue value, out int code) => _codeOf.TryGetValue(value, out code);

    /// <summary>
    /// Builds the column by reading every row once (design §3.3).
    /// </summary>
    /// <param name="rowCount">Number of rows in the dataset.</param>
    /// <param name="read">Reads one row; returns false for null.</param>
    /// <param name="comparer">Value equality; defaults to <see cref="ValueColumn.DefaultComparer{TValue}"/>.</param>
    public static ValueColumn<TValue> Build(int rowCount, RowReader<TValue> read, IEqualityComparer<TValue>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentNullException.ThrowIfNull(read);
        comparer ??= ValueColumn.DefaultComparer<TValue>();

        var codes = new int[rowCount];
        var dictionary = new List<TValue>();
        var codeOf = new Dictionary<TValue, int>(comparer);
        var totals = new List<int> { 0 };

        for (int row = 0; row < rowCount; row++)
        {
            if (!read(row, out TValue value))
            {
                totals[0]++;
                continue;
            }

            if (!codeOf.TryGetValue(value, out int code))
            {
                dictionary.Add(value);
                code = dictionary.Count;
                codeOf.Add(value, code);
                totals.Add(0);
            }

            codes[row] = code;
            totals[code]++;
        }

        return new ValueColumn<TValue>(codes, dictionary.ToArray(), codeOf, totals.ToArray());
    }

    /// <summary>
    /// Rows whose code is one of <paramref name="selectedCodes"/> (design §4.1). Code 0 selects the
    /// null rows (concept §4.8). One sequential scan of the codes with a mask lookup per row.
    /// </summary>
    public RowSet RowsWithCodes(ReadOnlySpan<int> selectedCodes)
    {
        if (selectedCodes.IsEmpty)
        {
            return RowSet.Empty(RowCount);
        }

        var mask = new bool[DistinctCount + 1];
        foreach (int code in selectedCodes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(code, nameof(selectedCodes));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(code, DistinctCount, nameof(selectedCodes));
            mask[code] = true;
        }

        var builder = new RowSetBuilder(RowCount);
        ReadOnlySpan<int> codes = _codes;
        for (int row = 0; row < codes.Length; row++)
        {
            if (mask[codes[row]])
            {
                builder.Set(row);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Filtered counts per code over the rows in <paramref name="context"/> (design §4.3).
    /// <paramref name="counts"/> must have length V + 1; it is cleared first. Index 0 is null.
    /// </summary>
    public void CountInto(RowSet context, Span<int> counts)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Length != RowCount)
        {
            throw new ArgumentException(
                $"Context has {context.Length} rows but the column has {RowCount}.", nameof(context));
        }

        if (counts.Length != DistinctCount + 1)
        {
            throw new ArgumentException(
                $"Counts span must have length {DistinctCount + 1} but has {counts.Length}.", nameof(counts));
        }

        if (context.IsFull)
        {
            _totalCounts.CopyTo(counts);
            return;
        }

        counts.Clear();
        ReadOnlySpan<int> codes = _codes;
        foreach (int row in context)
        {
            counts[codes[row]]++;
        }
    }
}
