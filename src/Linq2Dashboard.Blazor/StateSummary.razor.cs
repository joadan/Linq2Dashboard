using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// A plain rendering of the whole state: matching and total counts, every metric, and every facet with its
/// values, buckets, presets and the null value as clickable items that toggle like values (design §9). It is the
/// default child of <see cref="DashboardView{T}"/>, so a view with no content shows something complete, and it is
/// a way to see everything the state holds while laying out a page with the intent-carrying components.
/// </summary>
public partial class StateSummary<T>
{
    /// <summary>Text between the matching count and the total count.</summary>
    [Parameter]
    public string OfText { get; set; } = "of";

    /// <summary>Text of the button that clears every selection, shown when there is one.</summary>
    [Parameter]
    public string ClearAllText { get; set; } = "Clear all";

    /// <summary>Text of the button in a facet's heading that clears that facet, shown when it has a selection.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Label of the "Other" row that holds the values Top N left out (concept §6).</summary>
    [Parameter]
    public string OtherText { get; set; } = "Other";

    /// <summary>The null item of a range facet toggles the null rows beside the intervals (concept §4.8).</summary>
    private Task ToggleNull(RangeFacetState facet) => Context.SelectAsync(facet.Key, NullToggled(facet));

    /// <summary>The null item of a date facet toggles the null rows beside the parts (concept §4.8).</summary>
    private Task ToggleNull(DateFacetState facet) => Context.SelectAsync(facet.Key, NullToggled(facet));

    /// <summary>The link form of <see cref="ToggleNull(RangeFacetState)"/>, when the view renders links.</summary>
    private string? NullHref(RangeFacetState facet) => LinkTo(s => s.With(facet.Key, NullToggled(facet)));

    /// <summary>The link form of <see cref="ToggleNull(DateFacetState)"/>, when the view renders links.</summary>
    private string? NullHref(DateFacetState facet) => LinkTo(s => s.With(facet.Key, NullToggled(facet)));

    private static RangeSelection NullToggled(RangeFacetState facet) => (facet.Selection as RangeSelection ?? RangeSelection.Empty).ToggleNull();

    private static DateSelection NullToggled(DateFacetState facet) => (facet.Selection as DateSelection ?? DateSelection.Empty).ToggleNull();
}
