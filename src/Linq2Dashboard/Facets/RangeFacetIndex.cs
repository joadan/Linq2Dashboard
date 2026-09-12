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
}
