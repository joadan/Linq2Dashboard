namespace Linq2Dashboard.Blazor;

/// <summary>
/// Turns the culture-free values in a <see cref="DashboardState{T}"/> into text (design §9).
/// One instance is cascaded from <see cref="DashboardView{T}"/> to every component, so culture
/// enters the UI in exactly one place. To change labels, number formats or the null label, derive from
/// <see cref="DefaultDashboardFormatter"/> and override what you need. The interface itself may gain a
/// member in a minor version when a component needs new text, so a type that implements it directly
/// is not protected by compatibility: only the default formatter is.
/// </summary>
public interface IDashboardFormatter
{
    /// <summary>Label for the null facet value (concept §4.8).</summary>
    string NullLabel { get; }

    /// <summary>
    /// Orders labels when a component sorts by label (<see cref="FacetSort.Label"/>). It belongs here so that the
    /// order follows the formatter's culture, not the machine's: the core compares values ordinally and the UI
    /// orders text, and both must be deterministic for a given formatter. The default is a case-insensitive
    /// comparer for the formatter's culture.
    /// </summary>
    IComparer<string> LabelComparer { get; }

    /// <summary>
    /// A facet value as text. <paramref name="value"/> is the boxed value from <see cref="FacetValue.Value"/>, or null.
    /// Implementations should honour <see cref="ValueFacetState.LabelOf"/> when it returns a label (concept §5), as the default does.
    /// </summary>
    string FormatValue(FacetState facet, object? value);

    /// <summary>A total or filtered count.</summary>
    string FormatCount(int count);

    /// <summary>A metric's value; <see cref="MetricState.Value"/> is null when no row contributed.</summary>
    string FormatMetric(MetricState metric);

    /// <summary>A metric's share of its total as a percentage, for example "38 %" for 0.38 (concept §4.4).</summary>
    string FormatShare(double share);

    /// <summary>A range bucket, for example "100 – 500", "&lt; 100" or "≥ 1 000".</summary>
    string FormatRangeBucket(RangeBucket bucket);

    /// <summary>A date bucket at the facet's granularity, for example "Mar 2026" or "Week 12, 2026".</summary>
    string FormatDateBucket(DateBucket bucket, DateGranularity granularity);

    /// <summary>A relative preset, for example "Last 7 days".</summary>
    string FormatPreset(DatePreset preset);

    /// <summary>One numeric interval that does not coincide with a bucket, for example from a slider: "100 – 500", "≥ 100". The null rows are a part of their own, labelled by <see cref="NullLabel"/>.</summary>
    string FormatRangeInterval(RangeInterval interval);

    /// <summary>One date part that does not coincide with a bucket: an absolute interval, or a preset through <see cref="FormatPreset"/>.</summary>
    string FormatDateInterval(DateInterval interval);
}
