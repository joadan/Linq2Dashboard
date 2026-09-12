namespace Linq2Dashboard.Blazor;

/// <summary>How <see cref="RangeFacet{T}"/> and the date facet lay out their buckets.</summary>
public enum BucketLayout
{
    /// <summary>Vertical bars side by side, labels beneath. Reads as a distribution.</summary>
    Histogram,

    /// <summary>One row per bucket with the label, an inline bar and the count. Reads as a list and suits many buckets or long labels.</summary>
    List,
}
