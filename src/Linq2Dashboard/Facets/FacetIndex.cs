using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>
/// A built facet: its columns plus the operations the calculation pipeline needs (design §6).
/// Immutable and thread-safe. Non-generic so the pipeline can treat every facet the same way.
/// </summary>
internal abstract class FacetIndex
{
    protected FacetIndex(string key, string title, FacetKind kind, int rowCount)
    {
        Key = key;
        Title = title;
        Kind = kind;
        RowCount = rowCount;
    }

    public string Key { get; }

    public string Title { get; }

    public FacetKind Kind { get; }

    public int RowCount { get; }

    public FacetInfo Info => new(Key, Title, Kind);

    /// <summary>Rows matching <paramref name="selection"/> (design §4.1). Throws if the selection is of the wrong kind.</summary>
    public abstract RowSet RowsMatching(Selection selection);

    protected TSelection Expect<TSelection>(Selection selection) where TSelection : Selection
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection as TSelection ?? throw new ArgumentException(
            $"Facet '{Key}' is a {Kind} facet and expects a {typeof(TSelection).Name}, but got a {selection.GetType().Name}.",
            nameof(selection));
    }
}
