namespace Linq2Dashboard.Blazor;

/// <summary>Builds the count texts the facet components share (design §9).</summary>
internal static class CountText
{
    /// <summary>
    /// The tooltip on a value, bucket or preset: the filtered count, the total in parentheses and the
    /// filtered count's share of the total, "34 (100) 34.0 %" in the invariant culture. The share is left out when the total is zero.
    /// </summary>
    public static string Tooltip(IDashboardFormatter formatter, int filteredCount, int totalCount)
    {
        string counts = $"{formatter.FormatCount(filteredCount)} ({formatter.FormatCount(totalCount)})";
        return totalCount == 0 ? counts : $"{counts} {formatter.FormatShare((double)filteredCount / totalCount)}";
    }
}
