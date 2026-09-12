namespace Linq2Dashboard;

/// <summary>
/// Calendar period a date facet buckets by. Periods are computed in the facet's time zone
/// (concept §5). Weeks follow ISO 8601 and start on Monday.
/// </summary>
public enum DateGranularity
{
    Year,
    Month,
    Week,
    Day,
}
