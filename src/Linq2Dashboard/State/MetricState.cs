namespace Linq2Dashboard;

/// <summary>
/// A metric's value over the matching rows (concept §4.4). <see cref="Value"/> is null when no row
/// contributed. <see cref="Share"/> is the value as a fraction of the same aggregation over every row
/// after fixed filters, so 0.38 means 38 % of the total. It is defined for <see cref="Aggregation.Count"/>,
/// <see cref="Aggregation.Sum"/> and <see cref="Aggregation.Distinct"/> and null for the other
/// aggregations, when there is no value, or when the total is zero.
/// </summary>
public sealed record MetricState(string Key, string Title, Aggregation Aggregation, double? Value, double? Share)
{
    public bool HasValue => Value.HasValue;

    public bool HasShare => Share.HasValue;
}

/// <summary>One page of matching rows in the application-defined order (concept §3, design §4.7).</summary>
public sealed record ResultPage<T>(IReadOnlyList<T> Items, int PageIndex, int PageSize, int MatchingCount)
{
    /// <summary>Number of pages of <see cref="PageSize"/> needed for all matching rows.</summary>
    public int PageCount => PageSize == 0 ? 0 : (MatchingCount + PageSize - 1) / PageSize;

    public bool HasPrevious => PageIndex > 0;

    public bool HasNext => PageIndex + 1 < PageCount;
}
