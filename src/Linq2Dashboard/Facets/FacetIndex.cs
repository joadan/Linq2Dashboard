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

    /// <summary>
    /// Counts and presents the facet against <paramref name="context"/>, the rows with every other
    /// facet's selection applied (concept §4.2). <paramref name="selection"/> is this facet's own,
    /// used only for the selected flags (design §4.3–4.5).
    /// </summary>
    public abstract FacetState Present(RowSet context, Selection? selection);

    protected TSelection? ExpectOrNull<TSelection>(Selection? selection) where TSelection : Selection =>
        selection is null ? null : Expect<TSelection>(selection);

    protected TSelection Expect<TSelection>(Selection selection) where TSelection : Selection
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection as TSelection ?? throw new ArgumentException(
            $"Facet '{Key}' is a {Kind} facet and expects a {typeof(TSelection).Name}, but got a {selection.GetType().Name}.",
            nameof(selection));
    }
}
