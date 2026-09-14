namespace Linq2Dashboard;

/// <summary>The aggregations a metric can compute over the matching rows (concept §4.4).</summary>
public enum Aggregation
{
    /// <summary>Number of matching rows. Needs no property and is unaffected by null.</summary>
    Count,

    /// <summary>Sum of the values, skipping null.</summary>
    Sum,

    /// <summary>Sum divided by the number of matching rows that have a value, not by all matching rows.</summary>
    Average,

    /// <summary>Smallest non-null value.</summary>
    Min,

    /// <summary>Largest non-null value.</summary>
    Max,

    /// <summary>Number of distinct non-null values among the matching rows. Equality follows the value facet rules: strings ignore case unless a comparer is given.</summary>
    Distinct,

    /// <summary>A formula over the metrics defined before it, computed from their values rather than from rows. No value when any input has none or the result is not finite.</summary>
    Calculated,
}
