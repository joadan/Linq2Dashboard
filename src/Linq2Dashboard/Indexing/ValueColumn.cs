namespace Linq2Dashboard.Indexing;

/// <summary>Non-generic helpers for <see cref="ValueColumn{TValue}"/>.</summary>
internal static class ValueColumn
{
    /// <summary>
    /// The comparer a value facet uses when the builder does not supply one. Strings compare
    /// ordinal, ignoring case, so "Sweden" and "sweden" are one facet value (design §3.3).
    /// Everything else uses default equality.
    /// </summary>
    public static IEqualityComparer<TValue> DefaultComparer<TValue>()
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
/// <remarks>
/// <typeparamref name="TValue"/> may be a reference type or a <see cref="Nullable{T}"/>. Null never
/// reaches the dictionary: the <see cref="RowReader{TValue}"/> protocol reports null as "no value"
/// and <see cref="TryGetCode"/> guards it, so the dictionary's notnull constraint is satisfied in
/// practice even though it cannot be expressed on the type parameter.
/// </remarks>
#pragma warning disable CS8714 // Nullability of type argument doesn't match 'notnull' constraint (see remarks).
internal sealed class ValueColumn<TValue> : IValueColumn<TValue>
{
    private readonly int[] codes;
    private readonly TValue[] dictionary;
    private readonly Dictionary<TValue, int> codeOf;
    private readonly int[] totalCounts;

    private ValueColumn(int[] codes, TValue[] dictionary, Dictionary<TValue, int> codeOf, int[] totalCounts)
    {
        this.codes = codes;
        this.dictionary = dictionary;
        this.codeOf = codeOf;
        this.totalCounts = totalCounts;
    }

    /// <summary>Number of rows in the dataset.</summary>
    public int RowCount => codes.Length;

    /// <summary>Number of distinct non-null values, V. Valid codes are 0..V.</summary>
    public int DistinctCount => dictionary.Length;

    /// <summary>Rows whose value is null.</summary>
    public int NullCount => totalCounts[0];

    public bool HasNulls => NullCount > 0;

    /// <summary>One code per row. Sequential reads of this span are the counting hot path.</summary>
    public ReadOnlySpan<int> Codes => codes;

    /// <summary>Total count per code, index 0 being null. Computed once at build (concept §4.3).</summary>
    public ReadOnlySpan<int> TotalCounts => totalCounts;

    /// <inheritdoc />
    public int[] TotalCountsArray => totalCounts;

    public int CodeAt(int row) => codes[row];

    /// <summary>The value behind a non-null code (1..V). The first-seen spelling under the comparer.</summary>
    public TValue ValueOf(int code)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(code, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code, DistinctCount);
        return dictionary[code - 1];
    }

    public int TotalCount(int code)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(code);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code, DistinctCount);
        return totalCounts[code];
    }

    /// <summary>Looks up the code of a non-null value. False when the value does not occur in the dataset.</summary>
    public bool TryGetCode(TValue value, out int code)
    {
        if (value is null)
        {
            code = 0;
            return false;
        }

        return codeOf.TryGetValue(value, out code);
    }

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
        ReadOnlySpan<int> all = codes;
        for (int row = 0; row < all.Length; row++)
        {
            if (mask[all[row]])
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
    public void CountInto(RowSet context, Span<int> counts) =>
        CodeColumn.CountInto(codes, totalCounts, context, counts);
}
#pragma warning restore CS8714
