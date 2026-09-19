using System.Text.Json.Nodes;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard.Facets;

/// <summary>Built numeric range facet.</summary>
internal sealed class RangeFacetIndex : FacetIndex
{
    private readonly int[] totals;

    public RangeFacetIndex(string key, string name, RangeColumn column)
        : this(key, name, column, column.TotalCountsArray, column.RowCount)
    {
    }

    private RangeFacetIndex(string key, string name, RangeColumn column, int[] totals, int rowCount)
        : base(key, name, FacetKind.Range, rowCount)
    {
        Column = column;
        this.totals = totals;
    }

    public RangeColumn Column { get; }

    /// <summary>The union of the intervals' rows, plus the null rows when asked (concept §4.1, design §4.1).</summary>
    public override RowSet RowsMatching(Selection selection)
    {
        RangeSelection range = Expect<RangeSelection>(selection);
        RowSet? rows = null;
        foreach (RangeInterval interval in range.Intervals)
        {
            RowSet these = Column.RowsInInterval(interval.From, interval.To, interval.FromInclusive, interval.ToInclusive);
            rows = rows is null ? these : rows.Or(these);
        }

        if (range.IncludeNull)
        {
            rows = rows is null ? Column.Nulls : rows.Or(Column.Nulls);
        }

        return rows ?? RowSet.Empty(Column.RowCount);
    }

    /// <summary>Design §4.5: bucket counts over the context, a bucket is selected when one of the intervals fully covers it.</summary>
    public override FacetState Present(RowSet context, Selection? selection)
    {
        RangeSelection? range = ExpectOrNull<RangeSelection>(selection);
        var counts = new int[Column.BucketCount + 1];
        Column.CountInto(context, counts);

        var buckets = new RangeBucket[Column.BucketCount];
        for (int i = 0; i < buckets.Length; i++)
        {
            (double from, double to) = Column.Bucket(i);
            bool last = i == buckets.Length - 1;
            bool selected = range is not null && Covers(range, from, to, last);
            buckets[i] = new RangeBucket(from, to, totals[i + 1], counts[i + 1], selected);
        }

        var nullValue = new FacetValue(null, totals[0], counts[0], range is { IncludeNull: true });
        return new RangeFacetState(Key, Name, selection, context.Count, Column.Min, Column.Max, buckets, nullValue);
    }

    /// <inheritdoc />
    public override FacetIndex Scope(RowSet scope)
    {
        var scoped = new int[Column.BucketCount + 1];
        Column.CountInto(scope, scoped);
        return new RangeFacetIndex(Key, Name, Column, scoped, scope.Count);
    }

    /// <summary>
    /// Design §2.5: one interval writes its bounds and flags flat, defaults omitted; several write an
    /// <c>intervals</c> array of the same objects; <c>{ "onlyNull": true }</c> for the null rows alone.
    /// </summary>
    public override JsonObject Serialize(Selection selection)
    {
        RangeSelection range = Expect<RangeSelection>(selection);
        var json = new JsonObject();
        if (range.OnlyNulls)
        {
            json["onlyNull"] = true;
            return json;
        }

        if (range.Intervals.Count == 1)
        {
            WriteInterval(range.Intervals[0], json);
        }
        else
        {
            var intervals = new JsonArray();
            foreach (RangeInterval interval in range.Intervals)
            {
                var element = new JsonObject();
                WriteInterval(interval, element);
                intervals.Add(element);
            }

            json["intervals"] = intervals;
        }

        if (range.IncludeNull)
        {
            json["includeNull"] = true;
        }

        return json;
    }

    /// <summary>Reads either form. An interval that cannot be read is dropped; nothing readable and no null rows yields null.</summary>
    public override Selection? Deserialize(JsonObject json)
    {
        if (!JsonValues.TryGetBool(json, "onlyNull", false, out bool onlyNull)
            || !JsonValues.TryGetBool(json, "includeNull", false, out bool includeNull))
        {
            return null;
        }

        if (onlyNull)
        {
            return RangeSelection.OnlyNull;
        }

        var intervals = new List<RangeInterval>();
        if (json.TryGetPropertyValue("intervals", out JsonNode? node))
        {
            if (node is not JsonArray array)
            {
                return null;
            }

            foreach (JsonNode? element in array)
            {
                if (element is JsonObject item && ReadInterval(item) is RangeInterval interval)
                {
                    intervals.Add(interval);
                }
            }
        }
        else if (ReadInterval(json) is RangeInterval interval && (interval.From is not null || interval.To is not null))
        {
            // The flat form without a bound is nothing, not "every value"; an explicit unbounded interval is written in the array form.
            intervals.Add(interval);
        }

        return intervals.Count == 0 && !includeNull ? null : new RangeSelection(intervals, includeNull);
    }

    /// <summary>
    /// Writes the intervals comma-separated, each as <c>from..to</c> or in bracket notation when an end is exclusive,
    /// with <c>null</c> as one more item for the null rows; <c>null</c> alone for only the null rows (design §2.5).
    /// </summary>
    public override string SerializeQuery(Selection selection)
    {
        RangeSelection range = Expect<RangeSelection>(selection);
        IEnumerable<string> items = range.Intervals.Select(interval => QueryValues.FormatInterval(
            interval.From is double from ? QueryValues.FormatDouble(from) : string.Empty,
            interval.To is double to ? QueryValues.FormatDouble(to) : string.Empty,
            interval.FromInclusive,
            interval.ToInclusive));
        return string.Join(',', range.IncludeNull ? items.Append(QueryValues.NullToken) : items);
    }

    /// <summary>Reads the comma-separated form; a single number is the closed interval at that value. An item that does not parse drops the selection.</summary>
    public override Selection? DeserializeQuery(string value)
    {
        bool includeNull = false;
        var intervals = new List<RangeInterval>();
        foreach (string? token in QueryValues.SplitTokens(value))
        {
            if (token is null)
            {
                includeNull = true;
                continue;
            }

            if (ParseInterval(token) is not RangeInterval interval)
            {
                return null;
            }

            intervals.Add(interval);
        }

        return intervals.Count == 0 && !includeNull ? null : new RangeSelection(intervals, includeNull);
    }

    /// <summary>Whether one of the selected intervals contains every value the bucket can hold.</summary>
    internal static bool Covers(RangeSelection range, double from, double to, bool lastBucket)
    {
        foreach (RangeInterval interval in range.Intervals)
        {
            if (Covers(interval, from, to, lastBucket))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the interval contains every value the bucket can hold.</summary>
    internal static bool Covers(RangeInterval range, double from, double to, bool lastBucket)
    {
        bool lowOk = range.From is null
            || range.From < from
            || (range.From == from && range.FromInclusive);

        bool highOk;
        if (range.To is null)
        {
            highOk = true;
        }
        else if (double.IsPositiveInfinity(to))
        {
            highOk = false;
        }
        else if (lastBucket)
        {
            highOk = range.To > to || (range.To == to && range.ToInclusive);
        }
        else
        {
            highOk = range.To >= to;
        }

        return lowOk && highOk;
    }

    private static void WriteInterval(RangeInterval interval, JsonObject json)
    {
        if (interval.From is double from)
        {
            json["from"] = from;
        }

        if (interval.To is double to)
        {
            json["to"] = to;
        }

        if (!interval.FromInclusive)
        {
            json["fromInclusive"] = false;
        }

        if (!interval.ToInclusive)
        {
            json["toInclusive"] = false;
        }
    }

    private static RangeInterval? ReadInterval(JsonObject json)
    {
        if (!JsonValues.TryGetDouble(json, "from", out double? from)
            || !JsonValues.TryGetDouble(json, "to", out double? to)
            || !JsonValues.TryGetBool(json, "fromInclusive", true, out bool fromInclusive)
            || !JsonValues.TryGetBool(json, "toInclusive", true, out bool toInclusive))
        {
            return null;
        }

        if (from is double f && to is double t && f > t)
        {
            return null;
        }

        return new RangeInterval(from, to, fromInclusive, toInclusive);
    }

    private static RangeInterval? ParseInterval(string text)
    {
        string interval = QueryValues.StripBrackets(text, out bool fromInclusive, out bool toInclusive);
        int separator = interval.IndexOf(QueryValues.IntervalSeparator, StringComparison.Ordinal);
        string fromText = separator < 0 ? interval : interval[..separator];
        string toText = separator < 0 ? interval : interval[(separator + QueryValues.IntervalSeparator.Length)..];

        double? from = null;
        double? to = null;
        if (fromText.Length > 0)
        {
            if (!QueryValues.TryParseDouble(fromText, out double f))
            {
                return null;
            }

            from = f;
        }

        if (toText.Length > 0)
        {
            if (!QueryValues.TryParseDouble(toText, out double t))
            {
                return null;
            }

            to = t;
        }

        if (from is null && to is null)
        {
            return null;
        }

        if (from is double lo && to is double hi && lo > hi)
        {
            return null;
        }

        return new RangeInterval(from, to, fromInclusive, toInclusive);
    }
}
