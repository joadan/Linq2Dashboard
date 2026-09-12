using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>Configuration of a numeric range facet. The selector is already converted to nullable double (design §3.3).</summary>
internal sealed class RangeFacetDefinition<T> : FacetDefinition<T>
{
    private readonly Func<T, double?> _selector;

    public RangeFacetDefinition(string key, Func<T, double?> selector)
        : base(key, FacetKind.Range)
    {
        _selector = selector;
    }

    public RangeBucketing Bucketing { get; set; } = RangeBucketing.Auto(10);

    public override FacetIndex Build(T[] items, TimeProvider timeProvider)
    {
        Func<T, double?> selector = _selector;
        var column = RangeColumn.Build(items.Length, (int row, out double value) =>
        {
            double? read = selector(items[row]);
            value = read.GetValueOrDefault();
            return read.HasValue;
        }, Bucketing);

        return new RangeFacetIndex(Key, Title, column);
    }
}
