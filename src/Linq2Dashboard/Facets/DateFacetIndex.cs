using System.Text.Json.Nodes;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard.Facets;

/// <summary>Built date facet. Presets resolve against the dashboard's <see cref="TimeProvider"/> at each call (concept §5).</summary>
internal sealed class DateFacetIndex : FacetIndex
{
    private readonly int[] totals;
    private readonly RowSet? scope;

    public DateFacetIndex(string key, string name, DateColumn column, IReadOnlyList<DatePreset> presets, bool skipEmptyPresets, TimeProvider timeProvider)
        : this(key, name, column, presets, skipEmptyPresets, timeProvider, column.TotalCountsArray, null)
    {
    }

    private DateFacetIndex(string key, string name, DateColumn column, IReadOnlyList<DatePreset> presets, bool skipEmptyPresets, TimeProvider timeProvider, int[] totals, RowSet? scope)
        : base(key, name, FacetKind.Date, scope?.Count ?? column.RowCount)
    {
        Column = column;
        Presets = presets;
        SkipEmptyPresets = skipEmptyPresets;
        TimeProvider = timeProvider;
        this.totals = totals;
        this.scope = scope;
    }

    public DateColumn Column { get; }

    public IReadOnlyList<DatePreset> Presets { get; }

    public bool SkipEmptyPresets { get; }

    public TimeProvider TimeProvider { get; }

    /// <summary>The union of the parts' rows, presets resolved now, plus the null rows when asked (concept §4.1, design §4.1).</summary>
    public override RowSet RowsMatching(Selection selection)
    {
        DateSelection date = Expect<DateSelection>(selection);
        RowSet? rows = null;
        foreach (DateInterval interval in date.Intervals)
        {
            (DateTimeOffset? from, DateTimeOffset? to) = Resolve(interval);
            RowSet these = Column.RowsInInterval(from, to);
            rows = rows is null ? these : rows.Or(these);
        }

        if (date.IncludeNull)
        {
            rows = rows is null ? Column.Nulls : rows.Or(Column.Nulls);
        }

        return rows ?? RowSet.Empty(Column.RowCount);
    }

    /// <summary>Design §4.5: period counts over the context, presets resolved and counted, selected flags by coverage.</summary>
    public override FacetState Present(RowSet context, Selection? selection)
    {
        DateSelection? date = ExpectOrNull<DateSelection>(selection);
        var counts = new int[Column.BucketCount + 1];
        Column.CountInto(context, counts);

        var resolved = new List<(DateTimeOffset? From, DateTimeOffset? To)>(date?.Intervals.Count ?? 0);
        if (date is not null)
        {
            foreach (DateInterval interval in date.Intervals)
            {
                resolved.Add(Resolve(interval));
            }
        }

        var buckets = new DateBucket[Column.BucketCount];
        for (int i = 0; i < buckets.Length; i++)
        {
            (DateTimeOffset from, DateTimeOffset to) = Column.BucketInterval(i);
            bool selected = false;
            foreach ((DateTimeOffset? selectedFrom, DateTimeOffset? selectedTo) in resolved)
            {
                if (Covers(selectedFrom, selectedTo, from, to))
                {
                    selected = true;
                    break;
                }
            }

            buckets[i] = new DateBucket(from, to, Column.BucketStart(i), totals[i + 1], counts[i + 1], selected);
        }

        var presets = new List<PresetState>(Presets.Count);
        foreach (DatePreset preset in Presets)
        {
            (DateTimeOffset from, DateTimeOffset to) = ResolvePreset(preset);
            RowSet rows = Column.RowsInInterval(from, to);
            int total = scope is null ? rows.Count : rows.And(scope).Count;
            if (SkipEmptyPresets && total == 0)
            {
                // A preset no row falls in is not offered, the way a value no row has is not a
                // facet value (concept §5). Being selected does not bring it back.
                continue;
            }

            // Selected when one part is this preset, or an absolute interval that is exactly the preset's interval
            // (a click on the March bar lights "This month"). Coverage would light every preset inside a wide selection.
            bool selected = false;
            if (date is not null)
            {
                foreach (DateInterval interval in date.Intervals)
                {
                    if (interval.Preset == preset || (interval.Preset is null && interval.From == from && interval.To == to))
                    {
                        selected = true;
                        break;
                    }
                }
            }

            presets.Add(new PresetState(preset, from, to, total, rows.And(context).Count, selected));
        }

        var nullValue = new FacetValue(null, totals[0], counts[0], date is { IncludeNull: true });
        return new DateFacetState(Key, Name, selection, context.Count, Column.Granularity, Column.Zone, buckets, presets, nullValue);
    }

    /// <inheritdoc />
    public override FacetIndex Scope(RowSet scope)
    {
        var scoped = new int[Column.BucketCount + 1];
        Column.CountInto(scope, scoped);
        return new DateFacetIndex(Key, Name, Column, Presets, SkipEmptyPresets, TimeProvider, scoped, scope);
    }

    /// <summary>
    /// Design §2.5: one part writes <c>from</c>/<c>to</c> as ISO 8601 instants, or <c>preset</c> in camelCase, flat;
    /// several write an <c>intervals</c> array of the same objects; <c>{ "onlyNull": true }</c> for the null rows alone.
    /// </summary>
    public override JsonObject Serialize(Selection selection)
    {
        DateSelection date = Expect<DateSelection>(selection);
        var json = new JsonObject();
        if (date.OnlyNulls)
        {
            json["onlyNull"] = true;
            return json;
        }

        if (date.Intervals.Count == 1)
        {
            WriteInterval(date.Intervals[0], json);
        }
        else
        {
            var intervals = new JsonArray();
            foreach (DateInterval interval in date.Intervals)
            {
                var element = new JsonObject();
                WriteInterval(interval, element);
                intervals.Add(element);
            }

            json["intervals"] = intervals;
        }

        if (date.IncludeNull)
        {
            json["includeNull"] = true;
        }

        return json;
    }

    /// <summary>Reads either form. A part that cannot be read is dropped; nothing readable and no null rows yields null.</summary>
    public override Selection? Deserialize(JsonObject json)
    {
        if (!JsonValues.TryGetBool(json, "onlyNull", false, out bool onlyNull)
            || !JsonValues.TryGetBool(json, "includeNull", false, out bool includeNull))
        {
            return null;
        }

        if (onlyNull)
        {
            return DateSelection.OnlyNull;
        }

        var intervals = new List<DateInterval>();
        if (json.TryGetPropertyValue("intervals", out JsonNode? node))
        {
            if (node is not JsonArray array)
            {
                return null;
            }

            foreach (JsonNode? element in array)
            {
                if (element is JsonObject item && ReadInterval(item) is DateInterval interval)
                {
                    intervals.Add(interval);
                }
            }
        }
        else if (ReadInterval(json) is DateInterval interval && (interval.IsRelative || interval.From is not null || interval.To is not null))
        {
            // The flat form without a bound is nothing, not "every value"; an explicit unbounded interval is written in the array form.
            intervals.Add(interval);
        }

        return intervals.Count == 0 && !includeNull ? null : new DateSelection(intervals, includeNull);
    }

    /// <summary>
    /// Writes the parts comma-separated, each a preset name in camelCase or <c>from..to</c> in compact ISO 8601,
    /// with <c>null</c> as one more item for the null rows; <c>null</c> alone for only the null rows (design §2.5).
    /// </summary>
    public override string SerializeQuery(Selection selection)
    {
        DateSelection date = Expect<DateSelection>(selection);
        IEnumerable<string> items = date.Intervals.Select(interval => interval.Preset is DatePreset preset
            ? JsonValues.CamelCase(preset)
            : QueryValues.FormatInterval(
                interval.From is DateTimeOffset from ? QueryValues.FormatInstant(from) : string.Empty,
                interval.To is DateTimeOffset to ? QueryValues.FormatInstant(to) : string.Empty));
        return string.Join(',', date.IncludeNull ? items.Append(QueryValues.NullToken) : items);
    }

    /// <summary>Reads the comma-separated form: preset names, case-insensitively, or instant intervals. An item that does not parse drops the selection.</summary>
    public override Selection? DeserializeQuery(string value)
    {
        bool includeNull = false;
        var intervals = new List<DateInterval>();
        foreach (string? token in QueryValues.SplitTokens(value))
        {
            if (token is null)
            {
                includeNull = true;
                continue;
            }

            if (ParseInterval(token) is not DateInterval interval)
            {
                return null;
            }

            intervals.Add(interval);
        }

        return intervals.Count == 0 && !includeNull ? null : new DateSelection(intervals, includeNull);
    }

    /// <summary>The instant interval a preset means right now, in the facet's zone.</summary>
    public (DateTimeOffset From, DateTimeOffset To) ResolvePreset(DatePreset preset) =>
        DatePresets.Resolve(preset, TimeProvider.GetUtcNow(), Column.Zone);

    private (DateTimeOffset? From, DateTimeOffset? To) Resolve(DateInterval interval)
    {
        if (interval.Preset is DatePreset preset)
        {
            (DateTimeOffset from, DateTimeOffset to) = ResolvePreset(preset);
            return (from, to);
        }

        return (interval.From, interval.To);
    }

    private static bool Covers(DateTimeOffset? selectedFrom, DateTimeOffset? selectedTo, DateTimeOffset from, DateTimeOffset to) =>
        (selectedFrom is null || selectedFrom <= from) && (selectedTo is null || selectedTo >= to);

    private static void WriteInterval(DateInterval interval, JsonObject json)
    {
        if (interval.Preset is DatePreset preset)
        {
            json["preset"] = JsonValues.CamelCase(preset);
            return;
        }

        if (interval.From is DateTimeOffset from)
        {
            json["from"] = JsonValues.Instant(from);
        }

        if (interval.To is DateTimeOffset to)
        {
            json["to"] = JsonValues.Instant(to);
        }
    }

    private static DateInterval? ReadInterval(JsonObject json)
    {
        if (!JsonValues.TryGetEnum(json, "preset", out DatePreset? preset)
            || !JsonValues.TryGetInstant(json, "from", out DateTimeOffset? from)
            || !JsonValues.TryGetInstant(json, "to", out DateTimeOffset? to))
        {
            return null;
        }

        if (preset is DatePreset p)
        {
            return DateInterval.Relative(p);
        }

        if (from is DateTimeOffset f && to is DateTimeOffset t && f > t)
        {
            return null;
        }

        return DateInterval.Between(from, to);
    }

    private static DateInterval? ParseInterval(string text)
    {
        if (Enum.TryParse(text, ignoreCase: true, out DatePreset preset) && Enum.IsDefined(preset))
        {
            return DateInterval.Relative(preset);
        }

        int separator = text.IndexOf(QueryValues.IntervalSeparator, StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        string fromText = text[..separator];
        string toText = text[(separator + QueryValues.IntervalSeparator.Length)..];
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

        if (from is null && to is null)
        {
            return null;
        }

        if (from is DateTimeOffset lo && to is DateTimeOffset hi && lo > hi)
        {
            return null;
        }

        return DateInterval.Between(from, to);
    }
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
            DatePreset.ThisQuarter => Period(today, DateGranularity.Quarter),
            DatePreset.LastQuarter => PreviousPeriod(today, DateGranularity.Quarter),
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
