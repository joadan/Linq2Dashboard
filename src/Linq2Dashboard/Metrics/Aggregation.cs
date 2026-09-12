namespace Linq2Dashboard;

/// <summary>The aggregations a metric can compute over the matching rows (concept §4.4).</summary>
public enum Aggregation
{
    /// <summary>Number of matching rows. Needs no property and is unaffected by null.</summary>
    Count,

    Sum,

    /// <summary>Sum divided by the number of matching rows that have a value, not by all matching rows.</summary>
    Average,

    Min,

    Max,
}
