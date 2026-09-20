namespace Linq2Dashboard.Indexing;

/// <summary>
/// Dictionary-encoded column for a multi-valued facet (concept §5, design §3.3): every row holds a
/// set of codes rather than one. The codes of all rows lie in one flat array, and
/// <c>offsets[row]..offsets[row + 1]</c> is the row's slice, so a row with no values has an empty
/// slice and is the null value. A value is coded once per row however often the row repeats it,
/// and a null item inside a row's collection is skipped. Immutable once built.
/// </summary>
/// <remarks>
/// The dictionary is a <see cref="ValueColumn{TValue}"/> built over the deduplicated occurrences
/// rather than over the rows: its codes span is this column's flat array, and its dictionary and
/// lookup serve <see cref="ValueOf"/> and <see cref="TryGetCode"/>. Nothing about rows is asked of
/// it. That reuses the one place that maps values to codes, so the two column kinds cannot drift
/// apart on equality or first-seen spelling.
/// </remarks>
internal sealed class MultiValueColumn<TValue> : IValueColumn<TValue>
{
    private readonly int[] offsets;
    private readonly ValueColumn<TValue> occurrences;
    private readonly int[] totalCounts;

    private MultiValueColumn(int[] offsets, ValueColumn<TValue> occurrences, int[] totalCounts)
    {
        this.offsets = offsets;
        this.occurrences = occurrences;
        this.totalCounts = totalCounts;
    }

    /// <inheritdoc />
    public int RowCount => offsets.Length - 1;

    /// <inheritdoc />
    public int DistinctCount => occurrences.DistinctCount;

    /// <inheritdoc />
    public int NullCount => totalCounts[0];

    /// <summary>Number of (row, value) pairs after deduplication within rows: what counting is proportional to.</summary>
    public int OccurrenceCount => occurrences.RowCount;

    /// <summary>Total count per code, index 0 being the rows with no value. Computed once at build.</summary>
    public ReadOnlySpan<int> TotalCounts => totalCounts;

    /// <inheritdoc />
    public int[] TotalCountsArray => totalCounts;

    /// <summary>The codes of one row, in the order the row listed the values.</summary>
    public ReadOnlySpan<int> CodesOf(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
        return occurrences.Codes[offsets[row]..offsets[row + 1]];
    }

    /// <inheritdoc />
    public TValue ValueOf(int code) => occurrences.ValueOf(code);

    /// <inheritdoc />
    public bool TryGetCode(TValue value, out int code) => occurrences.TryGetCode(value, out code);

    /// <summary>
    /// Builds the column by reading every row's collection once (design §3.3). A null collection,
    /// an empty one, or one holding only nulls gives the row no codes, which is the null value.
    /// </summary>
    /// <param name="rowCount">Number of rows in the dataset.</param>
    /// <param name="read">Reads one row's values; may return null.</param>
    /// <param name="comparer">Value equality; defaults to <see cref="ValueColumn.DefaultComparer{TValue}"/>.</param>
    public static MultiValueColumn<TValue> Build(int rowCount, Func<int, IEnumerable<TValue>?> read, IEqualityComparer<TValue>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentNullException.ThrowIfNull(read);
        comparer ??= ValueColumn.DefaultComparer<TValue>();

        // Pass one: flatten, dropping nulls and a row's repeats of a value. One set, cleared per row.
        var offsets = new int[rowCount + 1];
        var flat = new List<TValue>();
        var seen = new HashSet<TValue>(comparer);
        for (int row = 0; row < rowCount; row++)
        {
            IEnumerable<TValue>? values = read(row);
            if (values is not null)
            {
                foreach (TValue value in values)
                {
                    if (value is not null && seen.Add(value))
                    {
                        flat.Add(value);
                    }
                }

                if (seen.Count > 0)
                {
                    seen.Clear();
                }
            }

            offsets[row + 1] = flat.Count;
        }

        // Pass two: code the occurrences. Every one is non-null, so the inner column's code 0 is never used.
        ValueColumn<TValue> occurrences = ValueColumn<TValue>.Build(flat.Count, (int i, out TValue value) =>
        {
            value = flat[i];
            return true;
        }, comparer);

        int[] totals = occurrences.TotalCountsArray.ToArray();
        for (int row = 0; row < rowCount; row++)
        {
            if (offsets[row] == offsets[row + 1])
            {
                totals[0]++;
            }
        }

        return new MultiValueColumn<TValue>(offsets, occurrences, totals);
    }

    /// <inheritdoc />
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

        bool nullSelected = mask[0];
        var builder = new RowSetBuilder(RowCount);
        ReadOnlySpan<int> codes = occurrences.Codes;
        for (int row = 0; row < RowCount; row++)
        {
            int start = offsets[row];
            int end = offsets[row + 1];
            if (start == end)
            {
                if (nullSelected)
                {
                    builder.Set(row);
                }

                continue;
            }

            for (int i = start; i < end; i++)
            {
                if (mask[codes[i]])
                {
                    builder.Set(row);
                    break;
                }
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Filtered counts per code over the rows in <paramref name="context"/> (design §4.3). Every code
    /// of a context row is incremented, so the counts sum to at least the context count (concept §5).
    /// </summary>
    public void CountInto(RowSet context, Span<int> counts)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Length != RowCount)
        {
            throw new ArgumentException(
                $"Context has {context.Length} rows but the column has {RowCount}.", nameof(context));
        }

        if (counts.Length != totalCounts.Length)
        {
            throw new ArgumentException(
                $"Counts span must have length {totalCounts.Length} but has {counts.Length}.", nameof(counts));
        }

        if (context.IsFull)
        {
            totalCounts.CopyTo(counts);
            return;
        }

        counts.Clear();
        ReadOnlySpan<int> codes = occurrences.Codes;
        foreach (int row in context)
        {
            int start = offsets[row];
            int end = offsets[row + 1];
            if (start == end)
            {
                counts[0]++;
                continue;
            }

            for (int i = start; i < end; i++)
            {
                counts[codes[i]]++;
            }
        }
    }
}
