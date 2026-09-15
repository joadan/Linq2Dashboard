using System.Text.Json.Nodes;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>
/// A built facet: its columns plus the operations the calculation pipeline needs (design §6).
/// Immutable and thread-safe. Non-generic so the pipeline can treat every facet the same way.
/// </summary>
internal abstract class FacetIndex
{
    protected FacetIndex(string key, string name, FacetKind kind, int rowCount)
    {
        Key = key;
        Name = name;
        Kind = kind;
        RowCount = rowCount;
    }

    public string Key { get; }

    public string Name { get; }

    public FacetKind Kind { get; }

    /// <summary>Rows in the dataset this index counts against: every row for an index built by the builder, the scope for one made by <see cref="Scope"/> (concept §4.10).</summary>
    public int RowCount { get; }

    public FacetInfo Info => new(Key, Name, Kind);

    /// <summary>Rows matching <paramref name="selection"/> (design §4.1). Throws if the selection is of the wrong kind.</summary>
    public abstract RowSet RowsMatching(Selection selection);

    /// <summary>
    /// Counts and presents the facet against <paramref name="context"/>, the rows with every other
    /// facet's selection applied (concept §4.2). <paramref name="selection"/> is this facet's own,
    /// used only for the selected flags (design §4.3–4.5).
    /// </summary>
    public abstract FacetState Present(RowSet context, Selection? selection);

    /// <summary>
    /// This facet over the rows in <paramref name="scope"/> (concept §4.10): the same columns and
    /// buckets, totals recounted over the scope. One count per facet; nothing else is copied.
    /// </summary>
    public abstract FacetIndex Scope(RowSet scope);

    /// <summary>Writes a selection of this facet's kind as its JSON shape (design §2.5).</summary>
    public abstract JsonObject Serialize(Selection selection);

    /// <summary>
    /// Reads a selection from its JSON shape. Returns null when the shape does not fit this facet or
    /// nothing usable remains, so a stale bookmark degrades to fewer selections (design §2.5).
    /// </summary>
    public abstract Selection? Deserialize(JsonObject json);

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
