using System.Linq.Expressions;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard.Facets;

/// <summary>Configuration of a value or boolean facet over a property of type <typeparamref name="TProp"/>.</summary>
internal sealed class ValueFacetDefinition<T, TProp> : FacetDefinition<T>
{
    private readonly Func<T, TProp> selector;

    public ValueFacetDefinition(string key, Expression<Func<T, TProp>> selector, FacetKind kind)
        : base(key, kind)
    {
        this.selector = selector.Compile();
    }

    public int? Top { get; set; }

    public RankMode RankMode { get; set; } = RankMode.FilteredCount;

    public bool Searchable { get; set; }

    public IEqualityComparer<TProp>? Comparer { get; set; }

    public ValueFormatter<TProp>? Formatter { get; set; }

    public Func<T, string?>? Label { get; set; }

    public override FacetIndex Build(T[] items, TimeProvider timeProvider, bool parallel)
    {
        var column = ValueColumn<TProp>.Build(items.Length, (int row, out TProp value) =>
        {
            value = selector(items[row]);
            return value is not null;
        }, Comparer);

        return new ValueFacetIndex<TProp>(Key, Name, Kind, column, Top, RankMode, Searchable, Formatter, BuildLabels(items, column));
    }

    /// <summary>
    /// One label per distinct value, read from the first row that introduces the value (concept §5),
    /// the same rule that fixes the presented spelling (design §3.3). Null when no label is defined.
    /// </summary>
    private string?[]? BuildLabels(T[] items, ValueColumn<TProp> column)
    {
        if (Label is null)
        {
            return null;
        }

        var labels = new string?[column.DistinctCount];
        var seen = new bool[column.DistinctCount];
        int remaining = labels.Length;
        ReadOnlySpan<int> codes = column.Codes;
        for (int row = 0; row < codes.Length && remaining > 0; row++)
        {
            int index = codes[row] - 1;
            if (index < 0 || seen[index])
            {
                continue;
            }

            seen[index] = true;
            labels[index] = Label(items[row]);
            remaining--;
        }

        return labels;
    }
}
