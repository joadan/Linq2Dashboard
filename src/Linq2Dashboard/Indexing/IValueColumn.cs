namespace Linq2Dashboard.Indexing;

/// <summary>
/// What a value facet needs from its column (design §3.3): the dictionary of distinct values, the
/// totals per code, and the two row operations. <see cref="ValueColumn{TValue}"/> holds one code
/// per row; <see cref="MultiValueColumn{TValue}"/> holds a set of codes per row (concept §5).
/// Code 0 is the null value in both.
/// </summary>
internal interface IValueColumn<TValue>
{
    /// <summary>Number of rows in the dataset.</summary>
    int RowCount { get; }

    /// <summary>Number of distinct non-null values, V. Valid codes are 0..V.</summary>
    int DistinctCount { get; }

    /// <summary>Rows that have the null value.</summary>
    int NullCount { get; }

    /// <summary>Total count per code, index 0 being null, as an array the facet index keeps without copying. Never written after build.</summary>
    int[] TotalCountsArray { get; }

    /// <summary>The value behind a non-null code (1..V). The first-seen spelling under the comparer.</summary>
    TValue ValueOf(int code);

    /// <summary>Looks up the code of a non-null value. False when the value does not occur in the dataset.</summary>
    bool TryGetCode(TValue value, out int code);

    /// <summary>Rows having any of <paramref name="selectedCodes"/> (design §4.1). Code 0 selects the null rows (concept §4.8).</summary>
    RowSet RowsWithCodes(ReadOnlySpan<int> selectedCodes);

    /// <summary>Filtered counts per code over the rows in <paramref name="context"/> (design §4.3). <paramref name="counts"/> must have length V + 1; it is cleared first.</summary>
    void CountInto(RowSet context, Span<int> counts);
}
