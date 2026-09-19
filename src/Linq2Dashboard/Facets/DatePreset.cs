namespace Linq2Dashboard;

/// <summary>
/// Relative date intervals a date facet can offer (concept §5). Each resolves to a half-open
/// instant interval at calculation time using the dashboard's <see cref="TimeProvider"/> and the
/// facet's time zone. "Today" is the calendar day containing now in that zone.
/// </summary>
public enum DatePreset
{
    /// <summary>[start of today, start of tomorrow)</summary>
    Today,

    /// <summary>[start of yesterday, start of today)</summary>
    Yesterday,

    /// <summary>The seven calendar days ending with today.</summary>
    Last7Days,

    /// <summary>The thirty calendar days ending with today.</summary>
    Last30Days,

    /// <summary>The ISO week containing today, Monday to Sunday.</summary>
    ThisWeek,

    /// <summary>The ISO week before the one containing today, Monday to Sunday.</summary>
    LastWeek,

    /// <summary>The calendar month containing today.</summary>
    ThisMonth,

    /// <summary>The calendar month before the one containing today.</summary>
    LastMonth,

    /// <summary>The calendar year containing today.</summary>
    ThisYear,

    /// <summary>The calendar year before the one containing today.</summary>
    LastYear,

    /// <summary>The calendar year containing today, up to and including today: [start of the year, start of tomorrow).</summary>
    YearToDate,
}
