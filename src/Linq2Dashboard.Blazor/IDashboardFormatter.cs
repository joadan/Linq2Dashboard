namespace Linq2Dashboard.Blazor;

/// <summary>
/// Turns the culture-free values in a <see cref="DashboardState{T}"/> into text (design §9).
/// One instance is cascaded from <see cref="DashboardView{T}"/> to every component, so culture
/// enters the UI in exactly one place. Implement it to change labels, number formats or the null
/// label; the default is <see cref="DefaultDashboardFormatter"/>.
/// </summary>
public interface IDashboardFormatter
{
    /// <summary>Label for the null facet value (concept §4.8).</summary>
    string NullLabel { get; }

    /// <summary>A facet value as text. <paramref name="value"/> is the boxed value from <see cref="FacetValue.Value"/>, or null.</summary>
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

    /// <summary>A numeric interval that does not coincide with a bucket, for example from a slider: "100 – 500", "≥ 100".</summary>
    string FormatRangeSelection(RangeSelection selection);

    /// <summary>An absolute date interval that does not coincide with a bucket or preset.</summary>
    string FormatDateSelection(DateSelection selection);
}
