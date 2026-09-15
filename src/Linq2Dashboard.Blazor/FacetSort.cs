namespace Linq2Dashboard.Blazor;

/// <summary>How <see cref="ValueFacet{T}"/> orders the values it shows (concept §6). Ranking in the core decides which values are presented; this decides their order on screen.</summary>
public enum FacetSort
{
    /// <summary>The core's rank order: by count, largest first, per the facet's <see cref="RankMode"/>. Default.</summary>
    Rank,

    /// <summary>Alphabetically by the text shown, as the formatter produces it, in the current culture and ignoring case. Null last.</summary>
    Label,

    /// <summary>By the value itself, using its natural comparison. The value type must implement <see cref="IComparable"/>. Null last.</summary>
    Value,
}
