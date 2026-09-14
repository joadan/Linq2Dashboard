using System.Globalization;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// Default text for dashboard values using a culture, the current one unless given. English labels
/// for null, booleans and presets; numbers without decimals when whole, otherwise two.
/// </summary>
public class DefaultDashboardFormatter : IDashboardFormatter
{
    private readonly CultureInfo? culture;

    public DefaultDashboardFormatter(CultureInfo? culture = null)
    {
        this.culture = culture;
    }

    public static DefaultDashboardFormatter Instance { get; } = new();

    protected CultureInfo Culture => culture ?? CultureInfo.CurrentCulture;

    public virtual string NullLabel => "(none)";

    /// <summary>An application-defined label (concept §5) wins; otherwise the value is formatted by type in the formatter's culture.</summary>
    public virtual string FormatValue(FacetState facet, object? value) => value switch
    {
        null => NullLabel,
        _ when facet is ValueFacetState values && values.LabelOf(value) is string label => label,
        bool b => b ? "Yes" : "No",
        DateTimeOffset dto => dto.ToString("g", Culture),
        DateTime dt => dt.ToString("g", Culture),
        DateOnly d => d.ToString("d", Culture),
        TimeOnly t => t.ToString("t", Culture),
        IFormattable f => f.ToString(null, Culture),
        _ => value.ToString() ?? string.Empty,
    };

    public virtual string FormatCount(int count) => count.ToString("N0", Culture);

    public virtual string FormatMetric(MetricState metric) =>
        metric.Value is double value ? FormatNumber(value) : "–";

    /// <summary>One decimal, so a small share does not round to zero.</summary>
    public virtual string FormatShare(double share) => share.ToString("P1", Culture);

    public virtual string FormatRangeBucket(RangeBucket bucket)
    {
        if (double.IsNegativeInfinity(bucket.From))
        {
            return $"< {FormatNumber(bucket.To)}";
        }

        if (double.IsPositiveInfinity(bucket.To))
        {
            return $"≥ {FormatNumber(bucket.From)}";
        }

        return $"{FormatNumber(bucket.From)} – {FormatNumber(bucket.To)}";
    }

    public virtual string FormatDateBucket(DateBucket bucket, DateGranularity granularity)
    {
        DateTime start = bucket.PeriodStart;
        return granularity switch
        {
            DateGranularity.Year => start.ToString("yyyy", Culture),
            DateGranularity.Month => start.ToString("MMM yyyy", Culture),
            DateGranularity.Week => $"Week {ISOWeek.GetWeekOfYear(start)}, {ISOWeek.GetYear(start)}",
            DateGranularity.Day => start.ToString("d", Culture),
            _ => start.ToString(Culture),
        };
    }

    public virtual string FormatPreset(DatePreset preset) => preset switch
    {
        DatePreset.Today => "Today",
        DatePreset.Yesterday => "Yesterday",
        DatePreset.Last7Days => "Last 7 days",
        DatePreset.Last30Days => "Last 30 days",
        DatePreset.ThisWeek => "This week",
        DatePreset.ThisMonth => "This month",
        DatePreset.ThisYear => "This year",
        _ => preset.ToString(),
    };

    public virtual string FormatRangeSelection(RangeSelection selection)
    {
        if (selection.OnlyNulls)
        {
            return NullLabel;
        }

        string text = (selection.From, selection.To) switch
        {
            (null, null) => "any",
            (double from, null) => $"{(selection.FromInclusive ? "≥" : ">")} {FormatNumber(from)}",
            (null, double to) => $"{(selection.ToInclusive ? "≤" : "<")} {FormatNumber(to)}",
            (double from, double to) => $"{FormatNumber(from)} – {FormatNumber(to)}",
        };

        return selection.IncludeNull ? $"{text} or {NullLabel}" : text;
    }

    public virtual string FormatDateSelection(DateSelection selection)
    {
        if (selection.OnlyNulls)
        {
            return NullLabel;
        }

        if (selection.Preset is DatePreset preset)
        {
            return FormatPreset(preset);
        }

        string text = (selection.From, selection.To) switch
        {
            (null, null) => "any",
            (DateTimeOffset from, null) => $"from {from.ToString("d", Culture)}",
            (null, DateTimeOffset to) => $"before {to.ToString("d", Culture)}",
            (DateTimeOffset from, DateTimeOffset to) => $"{from.ToString("d", Culture)} – {to.ToString("d", Culture)}",
        };

        return selection.IncludeNull ? $"{text} or {NullLabel}" : text;
    }

    /// <summary>Whole numbers without decimals, others with two.</summary>
    protected virtual string FormatNumber(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? value.ToString("N0", Culture)
            : value.ToString("N2", Culture);
}
