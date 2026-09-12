namespace Linq2Dashboard.Indexing;

/// <summary>
/// Shared operations over an <c>int[]</c> code column where code 0 means null and codes 1..V are
/// facet values or buckets. Value, range and date columns all count the same way (design §4.3).
/// </summary>
internal static class CodeColumn
{
    /// <summary>Total count per code over the whole dataset. Index 0 is null.</summary>
    public static int[] Totals(ReadOnlySpan<int> codes, int codeCount)
    {
        var totals = new int[codeCount];
        foreach (int code in codes)
        {
            totals[code]++;
        }

        return totals;
    }

    /// <summary>
    /// Filtered counts per code over the rows in <paramref name="context"/>. <paramref name="counts"/>
    /// must have the same length as <paramref name="totals"/>; it is cleared first. When the context
    /// is the full dataset the totals are copied instead of scanning.
    /// </summary>
    public static void CountInto(ReadOnlySpan<int> codes, ReadOnlySpan<int> totals, RowSet context, Span<int> counts)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Length != codes.Length)
        {
            throw new ArgumentException(
                $"Context has {context.Length} rows but the column has {codes.Length}.", nameof(context));
        }

        if (counts.Length != totals.Length)
        {
            throw new ArgumentException(
                $"Counts span must have length {totals.Length} but has {counts.Length}.", nameof(counts));
        }

        if (context.IsFull)
        {
            totals.CopyTo(counts);
            return;
        }

        counts.Clear();
        foreach (int row in context)
        {
            counts[codes[row]]++;
        }
    }
}
