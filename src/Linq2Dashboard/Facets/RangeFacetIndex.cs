using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>Built numeric range facet.</summary>
internal sealed class RangeFacetIndex : FacetIndex
{
    public RangeFacetIndex(string key, string title, RangeColumn column)
        : base(key, title, FacetKind.Range, column.RowCount)
    {
        Column = column;
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
            buckets[i] = new RangeBucket(from, to, Column.TotalCounts[i + 1], counts[i + 1], selected);
        }

        var nullValue = new FacetValue(null, Column.TotalCounts[0], counts[0], range is { IncludeNull: true });
        return new RangeFacetState(Key, Title, selection, context.Count, Column.Min, Column.Max, buckets, nullValue);
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
