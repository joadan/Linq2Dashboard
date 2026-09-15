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

    public override RowSet RowsMatching(Selection selection)
    {
        RangeSelection range = Expect<RangeSelection>(selection);
        if (range.OnlyNulls)
        {
            return Column.Nulls;
        }

        return Column.RowsInInterval(range.From, range.To, range.FromInclusive, range.ToInclusive, range.IncludeNull);
    }

    /// <summary>Design §4.5: bucket counts over the context, a bucket is selected when the interval fully covers it.</summary>
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
            bool selected = range is { OnlyNulls: false } && Covers(range, from, to, last);
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

    /// <summary>Design §2.5: bounds and flags, defaults omitted; <c>{ "onlyNull": true }</c> for the null rows alone.</summary>
    public override JsonObject Serialize(Selection selection)
    {
        RangeSelection range = Expect<RangeSelection>(selection);
        var json = new JsonObject();
        if (range.OnlyNulls)
        {
            json["onlyNull"] = true;
            return json;
        }

        if (range.From is double from)
        {
            json["from"] = from;
        }

        if (range.To is double to)
        {
            json["to"] = to;
        }

        if (!range.FromInclusive)
        {
            json["fromInclusive"] = false;
        }

        if (!range.ToInclusive)
        {
            json["toInclusive"] = false;
        }

        if (range.IncludeNull)
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
            return RangeSelection.OnlyNull;
        }

        if (!JsonValues.TryGetDouble(json, "from", out double? from)
            || !JsonValues.TryGetDouble(json, "to", out double? to)
            || !JsonValues.TryGetBool(json, "fromInclusive", true, out bool fromInclusive)
            || !JsonValues.TryGetBool(json, "toInclusive", true, out bool toInclusive)
            || !JsonValues.TryGetBool(json, "includeNull", false, out bool includeNull))
        {
            return null;
        }

        if (from is null && to is null && !includeNull)
        {
            return null;
        }

        if (from is double f && to is double t && f > t)
        {
            return null;
        }

        return new RangeSelection(from, to, fromInclusive, toInclusive, includeNull);
    }

    /// <summary>Whether the selected interval contains every value the bucket can hold.</summary>
    internal static bool Covers(RangeSelection range, double from, double to, bool lastBucket)
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
}
