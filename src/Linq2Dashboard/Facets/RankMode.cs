namespace Linq2Dashboard;

/// <summary>How a value facet ranks its values when only the top N are presented (concept §6).</summary>
public enum RankMode
{
    /// <summary>The list follows the current context and reorders on every click. Default.</summary>
    FilteredCount,

    /// <summary>The list is stable; some presented values may show a filtered count of zero.</summary>
    TotalCount,
}
