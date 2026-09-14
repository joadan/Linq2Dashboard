namespace Linq2Dashboard;

/// <summary>The kinds of facet (concept §5).</summary>
public enum FacetKind
{
    Value,
    Boolean,
    Range,
    Date,

    /// <summary>Free text matched by an application function. No values, only the text. Added 2026-09-14.</summary>
    Text,
}
