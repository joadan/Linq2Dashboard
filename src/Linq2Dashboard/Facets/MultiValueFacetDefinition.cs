using System.Linq.Expressions;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard.Facets;

/// <summary>
/// Configuration of a multi-valued facet over a collection property whose items are of type
/// <typeparamref name="TItem"/> (concept §5). Builds the same <see cref="ValueFacetIndex{TValue}"/>
/// as a value facet, over a <see cref="MultiValueColumn{TValue}"/>.
/// </summary>
internal sealed class MultiValueFacetDefinition<T, TItem> : FacetDefinition<T>
{
    private readonly Func<T, IEnumerable<TItem>?> selector;

    public MultiValueFacetDefinition(string key, Expression<Func<T, IEnumerable<TItem>?>> selector)
        : base(key, FacetKind.MultiValue)
    {
        this.selector = selector.Compile();
    }

    public int? Top { get; set; }

    public RankMode RankMode { get; set; } = RankMode.FilteredCount;

    public bool Searchable { get; set; }

    public IEqualityComparer<TItem>? Comparer { get; set; }

    public ValueFormatter<TItem>? Formatter { get; set; }

    public Func<TItem, string?>? Label { get; set; }

    public override FacetIndex Build(T[] items, TimeProvider timeProvider, bool parallel)
    {
        var column = MultiValueColumn<TItem>.Build(items.Length, row => selector(items[row]), Comparer);
        return new ValueFacetIndex<TItem>(Key, Name, Kind, column, Top, RankMode, Searchable, Formatter, BuildLabels(column));
    }

    /// <summary>
    /// One label per distinct value, read from the value itself (concept §5): a row has several
    /// values, so unlike a value facet's label the selector cannot read the row. Null when no label
    /// is defined.
    /// </summary>
    private string?[]? BuildLabels(MultiValueColumn<TItem> column)
    {
        if (Label is null)
        {
            return null;
        }

        var labels = new string?[column.DistinctCount];
        for (int code = 1; code <= column.DistinctCount; code++)
        {
            labels[code - 1] = Label(column.ValueOf(code));
        }

        return labels;
    }
}
