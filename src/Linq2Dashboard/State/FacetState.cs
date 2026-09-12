namespace Linq2Dashboard;

/// <summary>
/// What the UI sees of one facet after a calculation (concept §7, design §2.4). Non-generic,
/// immutable, and free of selectors, expressions and indexes.
/// </summary>
public abstract class FacetState
{
    private protected FacetState(string key, string title, FacetKind kind, Selection? selection, int contextCount)
    {
        Key = key;
        Title = title;
        Kind = kind;
        Selection = selection;
        ContextCount = contextCount;
    }

    /// <summary>Stable identity used in selections, state and bookmarks.</summary>
    public string Key { get; }

    public string Title { get; }

    public FacetKind Kind { get; }

    /// <summary>The current selection in this facet, or null when unconstrained.</summary>
    public Selection? Selection { get; }

    /// <summary>
    /// Rows in this facet's own counting context: every other facet's selection applied, this
    /// facet's own excluded (concept §4.2). Filtered counts under this facet sum to this number.
    /// </summary>
    public int ContextCount { get; }

    public bool HasSelection => Selection is not null;
}

/// <summary>State of a value or boolean facet.</summary>
public sealed class ValueFacetState : FacetState
{
    private readonly Func<string, int, IReadOnlyList<FacetValue>> _search;

    internal ValueFacetState(
        string key, string title, FacetKind kind, Selection? selection, int contextCount,
        IReadOnlyList<FacetValue> values, FacetCount? other, int distinctCount, bool isSearchable,
        Func<string, int, IReadOnlyList<FacetValue>> search)
        : base(key, title, kind, selection, contextCount)
    {
        Values = values;
        Other = other;
        DistinctCount = distinctCount;
        IsSearchable = isSearchable;
        _search = search;
    }

    /// <summary>
    /// Presented values in rank order, selected values always included (concept §6). Includes the
    /// null value when the dataset has nulls (concept §4.8) and values whose filtered count is zero
    /// (concept §4.3).
    /// </summary>
    public IReadOnlyList<FacetValue> Values { get; }

    /// <summary>Remainder when Top N truncated the list, measured against this facet's context; null when nothing was truncated.</summary>
    public FacetCount? Other { get; }

    /// <summary>Number of facet values in the dataset, null included, whether presented or not.</summary>
    public int DistinctCount { get; }

    /// <summary>Whether the facet was configured to offer a search box. <see cref="Search"/> works regardless.</summary>
    public bool IsSearchable { get; }

    /// <summary>
    /// Values whose text contains <paramref name="text"/> (ordinal, ignoring case), ranked like
    /// <see cref="Values"/>, with counts under the current context. A UI operation, not a selection
    /// (concept §4.5). The null value is never returned.
    /// </summary>
    public IReadOnlyList<FacetValue> Search(string text, int max = 20)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        return _search(text, max);
    }
}

/// <summary>State of a numeric range facet.</summary>
public sealed class RangeFacetState : FacetState
{
    internal RangeFacetState(
        string key, string title, Selection? selection, int contextCount,
        double min, double max, IReadOnlyList<RangeBucket> buckets, FacetValue @null)
        : base(key, title, FacetKind.Range, selection, contextCount)
    {
        Min = min;
        Max = max;
        Buckets = buckets;
        Null = @null;
    }

    /// <summary>Smallest value in the dataset; NaN when there is none. Fixed (concept §5).</summary>
    public double Min { get; }

    /// <summary>Largest value in the dataset; NaN when there is none. Fixed (concept §5).</summary>
    public double Max { get; }

    /// <summary>Buckets in ascending order, fixed at initialisation. Only the counts change (concept §5).</summary>
    public IReadOnlyList<RangeBucket> Buckets { get; }

    /// <summary>The null value, sitting beside the buckets (concept §4.8). Its <c>Value</c> is null.</summary>
    public FacetValue Null { get; }
}

/// <summary>State of a date facet.</summary>
public sealed class DateFacetState : FacetState
{
    internal DateFacetState(
        string key, string title, Selection? selection, int contextCount,
        DateGranularity granularity, TimeZoneInfo timeZone, IReadOnlyList<DateBucket> buckets,
        IReadOnlyList<PresetState> presets, FacetValue @null)
        : base(key, title, FacetKind.Date, selection, contextCount)
    {
        Granularity = granularity;
        TimeZone = timeZone;
        Buckets = buckets;
        Presets = presets;
        Null = @null;
    }

    public DateGranularity Granularity { get; }

    /// <summary>The zone periods and presets are computed in (concept §5).</summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>One bucket per calendar period present in the dataset, chronological.</summary>
    public IReadOnlyList<DateBucket> Buckets { get; }

    /// <summary>Configured presets, each resolved to its interval as of this calculation and counted.</summary>
    public IReadOnlyList<PresetState> Presets { get; }

    /// <summary>The null value, sitting beside the buckets (concept §4.8). Its <c>Value</c> is null.</summary>
    public FacetValue Null { get; }
}

/// <summary>
/// One value under a value facet (concept §4.3). <see cref="Value"/> is the facet's real value,
/// boxed, or null for the null value; the UI formats it.
/// </summary>
public sealed record FacetValue(object? Value, int TotalCount, int FilteredCount, bool Selected)
{
    public bool IsNull => Value is null;
}

/// <summary>A pair of counts without an identity of its own, used for "Other" (concept §6).</summary>
public sealed record FacetCount(int TotalCount, int FilteredCount);

/// <summary>
/// One bucket of a range facet, covering <c>[From, To)</c>; the last bucket also includes
/// <c>To</c>. Bounds may be infinite. <see cref="Selected"/> is true when the current interval
/// fully covers the bucket.
/// </summary>
public sealed record RangeBucket(double From, double To, int TotalCount, int FilteredCount, bool Selected)
{
    /// <summary>The selection a click on this bucket produces (design §2.2).</summary>
    public RangeSelection ToSelection() => new(
        double.IsInfinity(From) ? null : From,
        double.IsInfinity(To) ? null : To,
        fromInclusive: true,
        toInclusive: false);
}

/// <summary>
/// One calendar period of a date facet as the half-open instant interval <c>[From, To)</c>.
/// <see cref="PeriodStart"/> is the same start as a local date-time in the facet's zone, for labels.
/// </summary>
public sealed record DateBucket(DateTimeOffset From, DateTimeOffset To, DateTime PeriodStart, int TotalCount, int FilteredCount, bool Selected)
{
    /// <summary>The selection a click on this bucket produces (design §2.2).</summary>
    public DateSelection ToSelection() => DateSelection.Between(From, To);
}

/// <summary>A relative preset resolved as of this calculation (concept §5), with its counts.</summary>
public sealed record PresetState(DatePreset Preset, DateTimeOffset From, DateTimeOffset To, int TotalCount, int FilteredCount, bool Selected)
{
    public DateSelection ToSelection() => DateSelection.Relative(Preset);
}
