using System.Linq.Expressions;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>Configuration of a value or boolean facet over a property of type <typeparamref name="TProp"/>.</summary>
internal sealed class ValueFacetDefinition<T, TProp> : FacetDefinition<T>
{
    private readonly Func<T, TProp> _selector;

    public ValueFacetDefinition(string key, Expression<Func<T, TProp>> selector, FacetKind kind)
        : base(key, kind)
    {
        _selector = selector.Compile();
    }

    public int? Top { get; set; }

    public RankMode RankMode { get; set; } = RankMode.FilteredCount;

    public bool Searchable { get; set; }

    public IEqualityComparer<TProp>? Comparer { get; set; }

    public override FacetIndex Build(T[] items, TimeProvider timeProvider)
    {
        Func<T, TProp> selector = _selector;
        var column = ValueColumn<TProp>.Build(items.Length, (int row, out TProp value) =>
        {
            value = selector(items[row]);
            return value is not null;
        }, Comparer);

        return new ValueFacetIndex<TProp>(Key, Title, Kind, column, Top, RankMode, Searchable);
    }
}
