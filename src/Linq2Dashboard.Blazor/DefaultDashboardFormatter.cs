using System.Globalization;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// Default text for dashboard values using a culture, the current one unless given. English labels
/// for null, booleans and presets; numbers without decimals when whole, otherwise two.
/// </summary>
public class DefaultDashboardFormatter : IDashboardFormatter
{
    private readonly CultureInfo? culture;

    /// <summary>Creates a formatter for <paramref name="culture"/>, or for the current culture at each call when null.</summary>
    public DefaultDashboardFormatter(CultureInfo? culture = null)
    {
        this.culture = culture;
    }

    /// <summary>A shared instance that formats in the current culture.</summary>
    public static DefaultDashboardFormatter Instance { get; } = new();

    /// <summary>The culture to format in: the one given, else the current culture.</summary>
    protected CultureInfo Culture => culture ?? CultureInfo.CurrentCulture;

    /// <inheritdoc />
    public virtual string NullLabel => "(none)";

    /// <summary>Case-insensitive, in the formatter's culture, so ö sorts after z for Swedish and near o for the invariant culture.</summary>
    public virtual IComparer<string> LabelComparer => StringComparer.Create(Culture, ignoreCase: true);

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

    /// <inheritdoc />
    public virtual string FormatCount(int count) => count.ToString("N0", Culture);

    /// <inheritdoc />
    public virtual string FormatMetric(MetricState metric) =>
        metric.Value is double value ? FormatNumber(value) : "–";

    /// <summary>One decimal, so a small share does not round to zero.</summary>
    public virtual string FormatShare(double share) => share.ToString("P1", Culture);

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public virtual string FormatPreset(DatePreset preset) => preset switch
    {
        DatePreset.Today => "Today",
        DatePreset.Yesterday => "Yesterday",
        DatePreset.Last7Days => "Last 7 days",
        DatePreset.Last30Days => "Last 30 days",
        DatePreset.ThisWeek => "This week",
        DatePreset.LastWeek => "Last week",
        DatePreset.ThisMonth => "This month",
        DatePreset.LastMonth => "Last month",
        DatePreset.ThisYear => "This year",
        DatePreset.LastYear => "Last year",
        DatePreset.YearToDate => "Year to date",
        _ => preset.ToString(),
    };

    /// <inheritdoc />
    public virtual string FormatRangeInterval(RangeInterval interval) => (interval.From, interval.To) switch
    {
        (null, null) => "any",
        (double from, null) => $"{(interval.FromInclusive ? "≥" : ">")} {FormatNumber(from)}",
        (null, double to) => $"{(interval.ToInclusive ? "≤" : "<")} {FormatNumber(to)}",
        (double from, double to) => $"{FormatNumber(from)} – {FormatNumber(to)}",
    };

    /// <inheritdoc />
    public virtual string FormatDateInterval(DateInterval interval)
    {
        if (interval.Preset is DatePreset preset)
        {
            return FormatPreset(preset);
        }

        return (interval.From, interval.To) switch
        {
            (null, null) => "any",
            (DateTimeOffset from, null) => $"from {from.ToString("d", Culture)}",
            (null, DateTimeOffset to) => $"before {to.ToString("d", Culture)}",
            (DateTimeOffset from, DateTimeOffset to) => $"{from.ToString("d", Culture)} – {to.ToString("d", Culture)}",
        };
    }

    /// <summary>Whole numbers without decimals, others with two.</summary>
    protected virtual string FormatNumber(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? value.ToString("N0", Culture)
            : value.ToString("N2", Culture);
}
