namespace Linq2Dashboard;

/// <summary>The kinds of facet (concept §5).</summary>
public enum FacetKind
{
    /// <summary>Distinct values with counts: strings, enums, numbers, anything with equality (concept §5).</summary>
    Value,
    /// <summary>True and false, and null for a nullable member (concept §5).</summary>
    Boolean,
    /// <summary>A number bucketed into fixed intervals (concept §5).</summary>
    Range,
    /// <summary>A date bucketed into calendar periods, with relative presets (concept §5).</summary>
    Date,

    /// <summary>Free text matched by an application function. No values, only the text. Added 2026-09-14.</summary>
    Text,
}
