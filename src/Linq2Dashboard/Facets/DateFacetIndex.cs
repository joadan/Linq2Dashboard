using System.Text.Json.Nodes;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard.Facets;

/// <summary>Built date facet. Presets resolve against the dashboard's <see cref="TimeProvider"/> at each call (concept §5).</summary>
internal sealed class DateFacetIndex : FacetIndex
{
    private readonly int[] totals;
    private readonly RowSet? scope;

    public DateFacetIndex(string key, string name, DateColumn column, IReadOnlyList<DatePreset> presets, TimeProvider timeProvider)
        : this(key, name, column, presets, timeProvider, column.TotalCountsArray, null)
    {
    }

    private DateFacetIndex(string key, string name, DateColumn column, IReadOnlyList<DatePreset> presets, TimeProvider timeProvider, int[] totals, RowSet? scope)
        : base(key, name, FacetKind.Date, scope?.Count ?? column.RowCount)
    {
        Column = column;
        Presets = presets;
        TimeProvider = timeProvider;
        this.totals = totals;
        this.scope = scope;
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
            buckets[i] = new DateBucket(from, to, Column.BucketStart(i), totals[i + 1], counts[i + 1], selected);
        }

        var presets = new PresetState[Presets.Count];
        for (int i = 0; i < presets.Length; i++)
        {
            DatePreset preset = Presets[i];
            (DateTimeOffset from, DateTimeOffset to) = ResolvePreset(preset);
            RowSet rows = Column.RowsInInterval(from, to);
            int total = scope is null ? rows.Count : rows.And(scope).Count;
            // Selected when the selection is this preset, or an absolute interval that is exactly the preset's interval
            // (a click on the March bar lights "This month"). Coverage would light every preset inside a wide selection.
            bool selected = date?.Preset == preset || (hasInterval && date?.Preset is null && selectedFrom == from && selectedTo == to);
            presets[i] = new PresetState(preset, from, to, total, rows.And(context).Count, selected);
        }

        var nullValue = new FacetValue(null, totals[0], counts[0], date is { IncludeNull: true });
        return new DateFacetState(Key, Name, selection, context.Count, Column.Granularity, Column.Zone, buckets, presets, nullValue);
    }

    /// <inheritdoc />
    public override FacetIndex Scope(RowSet scope)
    {
        var scoped = new int[Column.BucketCount + 1];
        Column.CountInto(scope, scoped);
        return new DateFacetIndex(Key, Name, Column, Presets, TimeProvider, scoped, scope);
    }

    /// <summary>Design §2.5: <c>from</c>/<c>to</c> as ISO 8601 instants, or <c>preset</c> in camelCase; <c>{ "onlyNull": true }</c> for the null rows alone.</summary>
    public override JsonObject Serialize(Selection selection)
    {
        DateSelection date = Expect<DateSelection>(selection);
        var json = new JsonObject();
        if (date.OnlyNulls)
        {
            json["onlyNull"] = true;
            return json;
        }

        if (date.Preset is DatePreset preset)
        {
            json["preset"] = JsonValues.CamelCase(preset);
        }
        else
        {
            if (date.From is DateTimeOffset from)
            {
                json["from"] = JsonValues.Instant(from);
            }

            if (date.To is DateTimeOffset to)
            {
                json["to"] = JsonValues.Instant(to);
            }
        }

        if (date.IncludeNull)
        {
            json["includeNull"] = true;
        }

        return json;
    }

    public override Selection? Deserialize(JsonObject json)
    {
        if (!JsonValues.TryGetBool(json, "onlyNull", false, out bool onlyNull))
        {
            return null;
        }

        if (onlyNull)
        {
            return DateSelection.OnlyNull;
        }

        if (!JsonValues.TryGetBool(json, "includeNull", false, out bool includeNull)
            || !JsonValues.TryGetEnum(json, "preset", out DatePreset? preset)
            || !JsonValues.TryGetInstant(json, "from", out DateTimeOffset? from)
            || !JsonValues.TryGetInstant(json, "to", out DateTimeOffset? to))
        {
            return null;
        }

        if (preset is DatePreset p)
        {
            return DateSelection.Relative(p) with { IncludeNull = includeNull };
        }

        if (from is null && to is null && !includeNull)
        {
            return null;
        }

        if (from is DateTimeOffset f && to is DateTimeOffset t && f > t)
        {
            return null;
        }

        return DateSelection.Between(from, to) with { IncludeNull = includeNull };
    }

    /// <summary>Writes the preset name in camelCase or <c>from..to</c> in compact ISO 8601, with <c>,null</c> for the null rows; <c>null</c> alone for only the null rows (design §2.5).</summary>
    public override string SerializeQuery(Selection selection)
    {
        DateSelection date = Expect<DateSelection>(selection);
        if (date.OnlyNulls)
        {
            return QueryValues.NullToken;
        }

        string interval = date.Preset is DatePreset preset
            ? JsonValues.CamelCase(preset)
            : QueryValues.FormatInterval(
                date.From is DateTimeOffset from ? QueryValues.FormatInstant(from) : string.Empty,
                date.To is DateTimeOffset to ? QueryValues.FormatInstant(to) : string.Empty);
        return date.IncludeNull ? interval + "," + QueryValues.NullToken : interval;
    }

    /// <summary>Reads a preset name, case-insensitively, or an instant interval. Anything that does not parse drops the selection.</summary>
    public override Selection? DeserializeQuery(string value)
    {
        if (value == QueryValues.NullToken)
        {
            return DateSelection.OnlyNull;
        }

        if (!QueryValues.SplitNullSuffix(value, out string interval, out bool includeNull))
        {
            return null;
        }

        if (Enum.TryParse(interval, ignoreCase: true, out DatePreset preset) && Enum.IsDefined(preset))
        {
            return DateSelection.Relative(preset) with { IncludeNull = includeNull };
        }

        int separator = interval.IndexOf(QueryValues.IntervalSeparator, StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        string fromText = interval[..separator];
        string toText = interval[(separator + QueryValues.IntervalSeparator.Length)..];
        DateTimeOffset? from = null;
        DateTimeOffset? to = null;
        if (fromText.Length > 0)
        {
            if (!QueryValues.TryParseInstant(fromText, out DateTimeOffset f))
            {
                return null;
            }

            from = f;
        }

        if (toText.Length > 0)
        {
            if (!QueryValues.TryParseInstant(toText, out DateTimeOffset t))
            {
                return null;
            }

            to = t;
        }

        if (from is null && to is null && !includeNull)
        {
            return null;
        }

        if (from is DateTimeOffset lo && to is DateTimeOffset hi && lo > hi)
        {
            return null;
        }

        return DateSelection.Between(from, to) with { IncludeNull = includeNull };
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
            DatePreset.LastWeek => PreviousPeriod(today, DateGranularity.Week),
            DatePreset.ThisMonth => Period(today, DateGranularity.Month),
            DatePreset.LastMonth => PreviousPeriod(today, DateGranularity.Month),
            DatePreset.ThisYear => Period(today, DateGranularity.Year),
            DatePreset.LastYear => PreviousPeriod(today, DateGranularity.Year),
            DatePreset.YearToDate => (DateColumn.PeriodStart(today, DateGranularity.Year), today.AddDays(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };

        return (DateColumn.ToInstant(from, zone), DateColumn.ToInstant(to, zone));
    }

    private static (DateTime From, DateTime To) Period(DateTime day, DateGranularity granularity)
    {
        DateTime start = DateColumn.PeriodStart(day, granularity);
        return (start, DateColumn.NextPeriodStart(start, granularity));
    }

    private static (DateTime From, DateTime To) PreviousPeriod(DateTime day, DateGranularity granularity)
    {
        DateTime start = DateColumn.PeriodStart(day, granularity);

        // The day before this period starts is the last day of the previous one.
        return (DateColumn.PeriodStart(start.AddDays(-1), granularity), start);
    }
}
