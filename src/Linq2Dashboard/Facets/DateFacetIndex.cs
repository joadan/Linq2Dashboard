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

        (DateTimeOffset? from, DateTimeOffset? to) = Interval(date);
        return Column.RowsInInterval(from, to, date.IncludeNull);
    }

    /// <summary>Design §4.5: period counts over the context, presets resolved and counted, selected flags by coverage.</summary>
    public override FacetState Present(RowSet context, Selection? selection)
    {
        DateSelection? date = ExpectOrNull<DateSelection>(selection);
        var counts = new int[Column.BucketCount + 1];
        Column.CountInto(context, counts);

        (DateTimeOffset? selectedFrom, DateTimeOffset? selectedTo) = date is { OnlyNulls: false } ? Interval(date) : (null, null);
        bool hasInterval = date is { OnlyNulls: false };

        var buckets = new DateBucket[Column.BucketCount];
        for (int i = 0; i < buckets.Length; i++)
        {
            (DateTimeOffset from, DateTimeOffset to) = Column.BucketInterval(i);
            bool selected = hasInterval && Covers(selectedFrom, selectedTo, from, to);
            buckets[i] = new DateBucket(from, to, Column.BucketStart(i), Column.TotalCounts[i + 1], counts[i + 1], selected);
        }

        var presets = new PresetState[Presets.Count];
        for (int i = 0; i < presets.Length; i++)
        {
            DatePreset preset = Presets[i];
            (DateTimeOffset from, DateTimeOffset to) = ResolvePreset(preset);
            RowSet rows = Column.RowsInInterval(from, to);
            presets[i] = new PresetState(preset, from, to, rows.Count, rows.And(context).Count, date?.Preset == preset);
        }

        var nullValue = new FacetValue(null, Column.TotalCounts[0], counts[0], date is { IncludeNull: true });
        return new DateFacetState(Key, Title, selection, context.Count, Column.Granularity, Column.Zone, buckets, presets, nullValue);
    }

    /// <summary>The instant interval a preset means right now, in the facet's zone.</summary>
    public (DateTimeOffset From, DateTimeOffset To) ResolvePreset(DatePreset preset) =>
        DatePresets.Resolve(preset, TimeProvider.GetUtcNow(), Column.Zone);

    private (DateTimeOffset? From, DateTimeOffset? To) Interval(DateSelection date)
    {
        if (date.Preset is DatePreset preset)
        {
            (DateTimeOffset from, DateTimeOffset to) = ResolvePreset(preset);
            return (from, to);
        }

        return (date.From, date.To);
    }

    private static bool Covers(DateTimeOffset? selectedFrom, DateTimeOffset? selectedTo, DateTimeOffset from, DateTimeOffset to) =>
        (selectedFrom is null || selectedFrom <= from) && (selectedTo is null || selectedTo >= to);
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
