using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>A numeric range facet: fixed buckets as a histogram or a list, the null value beside them, and an optional slider (concept §5). A bucket click selects exactly its interval.</summary>
public partial class RangeFacet<T>
{
    private bool collapsed;
    private bool? lastCollapsedParameter;

    /// <summary>The facet key, as defined in the builder. Must be a range facet.</summary>
    [Parameter, EditorRequired]
    public string Key { get; set; } = default!;

    /// <summary>Overrides the name given in the builder, which is only a default display name, for example with a localised string (concept §7).</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Replaces the default header (name, bounds and clear button). Receives the facet state. Collapsing is then controlled only through <see cref="Collapsed"/>.</summary>
    [Parameter]
    public RenderFragment<RangeFacetState>? HeaderTemplate { get; set; }

    /// <summary>Let the user collapse the facet from its header. On by default; set false for a fixed header.</summary>
    [Parameter]
    public bool Collapsible { get; set; } = true;

    /// <summary>Whether the body is hidden. Bindable: <c>@@bind-Collapsed</c> follows the user's toggling, and setting it applies from the host.</summary>
    [Parameter]
    public bool Collapsed { get; set; }

    /// <summary>Raised when the user toggles the header; the second half of <c>@bind-Collapsed</c>.</summary>
    [Parameter]
    public EventCallback<bool> CollapsedChanged { get; set; }

    /// <summary>Vertical bars with labels beneath, or rows with an inline bar.</summary>
    [Parameter]
    public BucketLayout Layout { get; set; } = BucketLayout.Histogram;

    /// <summary>Show the dataset's min and max in the header (concept §5, fixed values).</summary>
    [Parameter]
    public bool ShowBounds { get; set; } = true;

    /// <summary>Show the count on each bucket. Default true.</summary>
    [Parameter]
    public bool ShowCounts { get; set; } = true;

    /// <summary>Show the total in parentheses after the filtered count, "filtered (total)". On by default; turn off to show the filtered count alone.</summary>
    [Parameter]
    public bool ShowTotals { get; set; } = true;

    /// <summary>
    /// A dual-handle slider beneath the buckets for a continuous interval (concept §5). Applies on
    /// release as a closed interval; dragging both handles to the ends clears the facet.
    /// </summary>
    [Parameter]
    public bool ShowSlider { get; set; }

    /// <summary>Slider step. Default: a power of ten near one hundredth of the range.</summary>
    [Parameter]
    public double? SliderStep { get; set; }

    /// <summary>Number inputs beside the slider for precise entry. Default true.</summary>
    [Parameter]
    public bool SliderInputs { get; set; } = true;

    /// <summary>Accessible label and tooltip of the header's clear button, which shows an ×.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Shown when the dataset has no value for this facet.</summary>
    [Parameter]
    public string NoValuesText { get; set; } = "No values";

    private string HeaderName => Name ?? Facet.Name;

    private RangeFacetState Facet => State.Facet(Key) as RangeFacetState
        ?? throw new InvalidOperationException($"Facet '{Key}' is not a range facet; use the component for its kind.");

    /// <summary>Slider handles follow the current interval selection; anything else puts them at the ends.</summary>
    private double? SliderFrom => Facet.Selection is RangeSelection { OnlyNulls: false } range ? range.From : null;

    private double? SliderTo => Facet.Selection is RangeSelection { OnlyNulls: false } range ? range.To : null;

    private IReadOnlyList<BucketBar> Bars()
    {
        RangeFacetState facet = Facet;
        var bars = new List<BucketBar>(facet.Buckets.Count + 1);
        foreach (RangeBucket bucket in facet.Buckets)
        {
            RangeBucket captured = bucket;
            bars.Add(new BucketBar(Formatter.FormatRangeBucket(bucket), bucket.TotalCount, bucket.FilteredCount, bucket.Selected, false, () => ClickBucket(captured)));
        }

        if (facet.Null.TotalCount > 0)
        {
            bars.Add(new BucketBar(Formatter.NullLabel, facet.Null.TotalCount, facet.Null.FilteredCount, facet.Null.Selected, true, ClickNull));
        }

        return bars;
    }

    /// <summary>A click selects the bucket's interval; a second click on the selected bucket clears the facet.</summary>
    private Task ClickBucket(RangeBucket bucket)
    {
        RangeSelection selection = bucket.ToSelection();
        return selection.Equals(Facet.Selection) ? Context.ClearAsync(Key) : Context.SelectAsync(Key, selection);
    }

    private Task ClickNull() =>
        Facet.Selection is RangeSelection { OnlyNulls: true } ? Context.ClearAsync(Key) : Context.SelectAsync(Key, RangeSelection.OnlyNull);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Collapsed != lastCollapsedParameter)
        {
            lastCollapsedParameter = Collapsed;
            collapsed = Collapsed;
        }
    }

    private Task ToggleCollapsed()
    {
        collapsed = !collapsed;
        lastCollapsedParameter = collapsed;
        return CollapsedChanged.InvokeAsync(collapsed);
    }

    private Task ApplySlider((double From, double To) interval)
    {
        RangeFacetState facet = Facet;
        if (interval.From <= facet.Min && interval.To >= facet.Max)
        {
            return Context.ClearAsync(Key);
        }

        return Context.SelectAsync(Key, RangeSelection.Between(interval.From, interval.To));
    }
}
