using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>Built date facet. Presets resolve against the dashboard's <see cref="TimeProvider"/> at each call (concept §5).</summary>
internal sealed class DateFacetIndex : FacetIndex
{
    public DateFacetIndex(string key, string title, DateColumn column, IReadOnlyList<DatePreset> presets, TimeProvider timeProvider)
        : base(key, title, FacetKind.Date, column.RowCount)
    {
        Column = column;
        Presets = presets;
        TimeProvider = timeProvider;
    }

    public DateColumn Column { get; }

    public IReadOnlyList<DatePreset> Presets { get; }

    public TimeProvider TimeProvider { get; }

    public override RowSet RowsMatching(Selection selection)
    {
        DateSelection date = Expect<DateSelection>(selection);
        if (date.OnlyNulls)
        {
            return Column.Nulls;
        }

        if (date.Preset is DatePreset preset)
        {
            (DateTimeOffset from, DateTimeOffset to) = ResolvePreset(preset);
            return Column.RowsInInterval(from, to, date.IncludeNull);
        }

        return Column.RowsInInterval(date.From, date.To, date.IncludeNull);
    }

    /// <summary>The instant interval a preset means right now, in the facet's zone.</summary>
    public (DateTimeOffset From, DateTimeOffset To) ResolvePreset(DatePreset preset) =>
        DatePresets.Resolve(preset, TimeProvider.GetUtcNow(), Column.Zone);
}

/// <summary>Turns a relative preset into a half-open instant interval (concept §5).</summary>
internal static class DatePresets
{
    public static (DateTimeOffset From, DateTimeOffset To) Resolve(DatePreset preset, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).DateTime.Date;
        (DateTime from, DateTime to) = preset switch
        {
            DatePreset.Today => (today, today.AddDays(1)),
            DatePreset.Yesterday => (today.AddDays(-1), today),
            DatePreset.Last7Days => (today.AddDays(-6), today.AddDays(1)),
            DatePreset.Last30Days => (today.AddDays(-29), today.AddDays(1)),
            DatePreset.ThisWeek => Period(today, DateGranularity.Week),
            DatePreset.ThisMonth => Period(today, DateGranularity.Month),
            DatePreset.ThisYear => Period(today, DateGranularity.Year),
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

        return (DateColumn.ToInstant(from, zone), DateColumn.ToInstant(to, zone));
    }

    private static (DateTime From, DateTime To) Period(DateTime day, DateGranularity granularity)
    {
        DateTime start = DateColumn.PeriodStart(day, granularity);
        return (start, DateColumn.NextPeriodStart(start, granularity));
    }
}
