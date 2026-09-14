namespace Linq2Dashboard.Indexing;

/// <summary>
/// Column for a distinct-count metric (design §3.3): one integer code per row, 0 for null and
/// 1..V for the distinct non-null values in first-seen order, exactly as a value column encodes
/// them. Only equality matters to the metric, so the dictionary is dropped after the build and the
/// column costs four bytes per row. Immutable once built.
/// </summary>
internal sealed class DistinctColumn
{
    private readonly int[] codes;

    private DistinctColumn(int[] codes, int distinctCount)
    {
        this.codes = codes;
        DistinctCount = distinctCount;
    }

    public int RowCount => codes.Length;

    /// <summary>Distinct non-null values over the whole dataset, V.</summary>
    public int DistinctCount { get; }

    /// <summary>Encodes one value per row. <paramref name="comparer"/> defaults to <see cref="ValueColumn.DefaultComparer{TValue}"/>, so strings ignore case.</summary>
    public static DistinctColumn Build<TValue>(int rowCount, RowReader<TValue> read, IEqualityComparer<TValue>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentNullException.ThrowIfNull(read);

        var codes = new int[rowCount];
#pragma warning disable CS8714 // Null never reaches the dictionary: the reader reports it as "no value" (see ValueColumn).
        var codeOf = new Dictionary<TValue, int>(comparer ?? ValueColumn.DefaultComparer<TValue>());
#pragma warning restore CS8714
        for (int row = 0; row < rowCount; row++)
        {
            if (!read(row, out TValue value))
            {
                continue;
            }

            if (!codeOf.TryGetValue(value, out int code))
            {
                code = codeOf.Count + 1;
                codeOf.Add(value, code);
            }

            codes[row] = code;
        }

        return new DistinctColumn(codes, codeOf.Count);
    }

    /// <summary>
    /// Distinct non-null values among the rows in <paramref name="context"/> (concept §4.4, design §4.6).
    /// One pass over the context marking each code seen in a bit set; the full dataset answers
    /// from <see cref="DistinctCount"/> without a scan.
    /// </summary>
    public int CountIn(RowSet context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Length != RowCount)
        {
            throw new ArgumentException($"Context has {context.Length} rows but the column has {RowCount}.", nameof(context));
        }

        if (context.IsFull)
        {
            return DistinctCount;
        }

        var seen = new ulong[(DistinctCount >> 6) + 1];
        int distinct = 0;
        foreach (int row in context)
        {
            int code = codes[row];
            if (code == 0)
            {
                continue;
            }

            ulong bit = 1UL << (code & 63);
            ref ulong word = ref seen[code >> 6];
            if ((word & bit) == 0)
            {
                word |= bit;
                distinct++;
            }
        }

        return distinct;
    }
}
