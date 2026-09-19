using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// Chips for the current selections, each removable, with a clear-all button (design §9). A facet's selection is a set of
/// parts, values, intervals, presets, the null rows or a text, and every part is removable on its own. Text comes from the
/// formatter, so chips read like the facets.
/// </summary>
public partial class ActiveSelections<T>
{
    /// <summary>Render nothing when there is no selection. Default true.</summary>
    [Parameter]
    public bool HideWhenEmpty { get; set; } = true;

    /// <summary>Prefix each chip with its facet's name. Default true.</summary>
    [Parameter]
    public bool ShowFacetName { get; set; } = true;

    /// <summary>
    /// One chip per facet with its selected parts inside, each removable on its own, instead of one
    /// chip per part. Default true: the parts of one facet are one OR group (concept §4.1). A facet with a
    /// single part renders a plain chip either way.
    /// </summary>
    [Parameter]
    public bool GroupValues { get; set; } = true;

    /// <summary>Text between parts in a grouped chip. Default ", "; " or " states the semantics.</summary>
    [Parameter]
    public string ValueSeparator { get; set; } = ", ";

    /// <summary>Shown when nothing is selected and <see cref="HideWhenEmpty"/> is false.</summary>
    [Parameter]
    public string EmptyText { get; set; } = "No selections";

    /// <summary>Accessible label and tooltip of the clear-all button, which shows an ×.</summary>
    [Parameter]
    public string ClearAllText { get; set; } = "Clear all";

    /// <summary>Accessible label of each chip's remove button.</summary>
    [Parameter]
    public string RemoveText { get; set; } = "Remove";

    /// <summary>One removable piece of a facet's selection and what removing it does.</summary>
    private readonly record struct ChipPart(string Label, Func<Task> Remove);

    /// <summary>
    /// The facet's selection as removable parts: each value, each interval or preset, the null rows, or the text.
    /// Removing a part toggles it off; for the one part of a text facet that clears the facet.
    /// </summary>
    private IReadOnlyList<ChipPart> Parts(FacetState facet)
    {
        switch (facet.Selection)
        {
            case null:
                return [];

            case ValueSelection values:
                return values.Values
                    .Select(value => new ChipPart(Formatter.FormatValue(facet, value), () => Context.ToggleAsync(facet.Key, value)))
                    .ToList();

            case RangeSelection range when facet is RangeFacetState state:
                var intervals = range.Intervals
                    .Select(interval => new ChipPart(RangeLabel(state, interval), () => Context.ToggleIntervalAsync(facet.Key, interval)))
                    .ToList();
                if (range.IncludeNull)
                {
                    intervals.Add(new ChipPart(Formatter.NullLabel, () => Context.SelectAsync(facet.Key, range.ToggleNull())));
                }

                return intervals;

            case DateSelection date when facet is DateFacetState state:
                var parts = date.Intervals
                    .Select(interval => new ChipPart(DateLabel(state, interval), () => Context.ToggleIntervalAsync(facet.Key, interval)))
                    .ToList();
                if (date.IncludeNull)
                {
                    parts.Add(new ChipPart(Formatter.NullLabel, () => Context.SelectAsync(facet.Key, date.ToggleNull())));
                }

                return parts;

            case TextSelection text:
                return [new ChipPart(text.Text, () => Context.ClearAsync(facet.Key))];

            default:
                return [new ChipPart(facet.Selection?.ToString() ?? string.Empty, () => Context.ClearAsync(facet.Key))];
        }
    }

    /// <summary>The bucket's own label when the interval is exactly that bucket, otherwise the formatter's interval text.</summary>
    private string RangeLabel(RangeFacetState state, RangeInterval interval) =>
        state.Buckets.FirstOrDefault(b => b.ToInterval().Equals(interval)) is RangeBucket bucket
            ? Formatter.FormatRangeBucket(bucket)
            : Formatter.FormatRangeInterval(interval);

    /// <summary>The preset's or period's own label when the part is exactly that, otherwise the formatter's interval text.</summary>
    private string DateLabel(DateFacetState state, DateInterval interval)
    {
        if (interval.Preset is DatePreset preset)
        {
            return Formatter.FormatPreset(preset);
        }

        return state.Buckets.FirstOrDefault(b => b.ToInterval().Equals(interval)) is DateBucket bucket
            ? Formatter.FormatDateBucket(bucket, state.Granularity)
            : Formatter.FormatDateInterval(interval);
    }
}
