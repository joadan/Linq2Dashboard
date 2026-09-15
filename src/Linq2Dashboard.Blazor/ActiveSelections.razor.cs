using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>Chips for the current selections, one per facet or per value, each removable, with a clear-all button (design §9). Text comes from the formatter, so chips read like the facets.</summary>
public partial class ActiveSelections<T>
{
    /// <summary>Render nothing when there is no selection. Default true.</summary>
    [Parameter]
    public bool HideWhenEmpty { get; set; } = true;

    /// <summary>Prefix each chip with its facet's title. Default true.</summary>
    [Parameter]
    public bool ShowFacetTitle { get; set; } = true;

    /// <summary>
    /// One chip per facet with its selected values inside, each removable on its own, instead of one
    /// chip per value. Default true: the values of one facet are one OR group (concept §4.1).
    /// </summary>
    [Parameter]
    public bool GroupValues { get; set; } = true;

    /// <summary>Text between values in a grouped chip. Default ", "; " or " states the semantics.</summary>
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

    /// <summary>The bucket's or preset's own label when the interval is exactly that, otherwise the formatter's interval text; a text facet's text as typed.</summary>
    private string SelectionLabel(FacetState facet)
    {
        switch (facet)
        {
            case TextFacetState when facet.Selection is TextSelection text:
                return text.Text;

            case RangeFacetState range when facet.Selection is RangeSelection selection:
                if (selection.OnlyNulls)
                {
                    return Formatter.NullLabel;
                }

                RangeBucket? bucket = range.Buckets.FirstOrDefault(b => b.ToSelection().Equals(selection));
                return bucket is not null ? Formatter.FormatRangeBucket(bucket) : Formatter.FormatRangeSelection(selection);

            case DateFacetState dates when facet.Selection is DateSelection selection:
                if (selection.OnlyNulls)
                {
                    return Formatter.NullLabel;
                }

                if (selection.Preset is DatePreset preset)
                {
                    return Formatter.FormatPreset(preset);
                }

                DateBucket? period = dates.Buckets.FirstOrDefault(b => b.ToSelection().Equals(selection));
                return period is not null ? Formatter.FormatDateBucket(period, dates.Granularity) : Formatter.FormatDateSelection(selection);

            default:
                return facet.Selection?.ToString() ?? string.Empty;
        }
    }
}
