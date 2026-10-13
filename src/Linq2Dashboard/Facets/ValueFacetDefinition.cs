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

    public Func<T, string?>? GroupBy { get; set; }

    public override FacetIndex Build(T[] items, TimeProvider timeProvider, bool parallel)
    {
        var column = ValueColumn<TProp>.Build(items.Length, (int row, out TProp value) =>
        {
            value = selector(items[row]);
            return value is not null;
        }, Comparer);

        int[]? firstRows = Label is null && GroupBy is null ? null : FirstRows(column);
        string?[]? labels = Label is { } label ? firstRows!.Select(row => label(items[row])).ToArray() : null;
        ValueGroups? groups = GroupBy is { } group ? ValueGroups.Build(items, firstRows!, group) : null;
        return new ValueFacetIndex<TProp>(Key, Name, Kind, column, Top, RankMode, Searchable, Formatter, labels, groups);
    }

    /// <summary>
    /// The row that introduces each distinct value, indexed by code − 1: labels and groups are read
    /// from it (concept §5, §6), the same rule that fixes the presented spelling (design §3.3).
    /// </summary>
    private static int[] FirstRows(ValueColumn<TProp> column)
    {
        var firstRows = new int[column.DistinctCount];
        var seen = new bool[column.DistinctCount];
        int remaining = firstRows.Length;
        ReadOnlySpan<int> codes = column.Codes;
        for (int row = 0; row < codes.Length && remaining > 0; row++)
        {
            int index = codes[row] - 1;
            if (index < 0 || seen[index])
            {
                continue;
            }

            seen[index] = true;
            firstRows[index] = row;
            remaining--;
        }

        return firstRows;
    }
}
