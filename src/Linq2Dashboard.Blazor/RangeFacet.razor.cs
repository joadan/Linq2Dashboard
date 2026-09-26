using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>A numeric range facet: fixed buckets as a histogram or a list, the null value beside them, and an optional slider (concept §5). A bucket click toggles exactly its interval, so bars select like values.</summary>
public partial class RangeFacet<T>
{
    private bool collapsed;
    private bool? lastCollapsedParameter;


    /// <summary>
    /// Overrides the name given in the builder, which is only a default display name, for example with a localised string
    /// (concept §7). When this component defines the facet under a view with <c>Items</c>, it is the facet's name (design §9).
    /// </summary>
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

    /// <summary>Show the total in parentheses after the filtered count, "filtered (total)". Off by default: the two numbers are equal until another facet narrows the set, and the tooltip always carries both (design §9).</summary>
    [Parameter]
    public bool ShowTotals { get; set; }

    /// <summary>
    /// A dual-handle slider beneath the buckets for a continuous interval (concept §5). Applies on
    /// release as a closed interval that replaces the facet's selection; dragging both handles to the ends clears the facet.
    /// </summary>
    [Parameter]
    public bool ShowSlider { get; set; }

    /// <summary>Slider step. Default: a power of ten near one hundredth of the range.</summary>
    [Parameter]
    public double? SliderStep { get; set; }

    /// <summary>Number inputs beside the slider for precise entry. Default true.</summary>
    [Parameter]
    public bool ShowSliderInputs { get; set; } = true;

    /// <summary>
    /// Classes for the slider's two number inputs, replacing the library's default look (<c>l2d-input</c>) so a
    /// CSS framework's class takes over cleanly, for example <c>form-control</c>. The hook classes
    /// <c>l2d-slider-input-from</c> and <c>l2d-slider-input-to</c> stay; the range inputs are not affected (design §9).
    /// </summary>
    [Parameter]
    public string? InputClass { get; set; }

    /// <summary>Accessible label and tooltip of the header's clear button, which shows an ×.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Shown when the dataset has no value for this facet.</summary>
    [Parameter]
    public string NoValuesText { get; set; } = "No values";

    private string HeaderName => Name ?? Facet.Name;

    private RangeFacetState Facet => State.Facet(ResolvedKey) as RangeFacetState
        ?? throw WrongKind(State.Facet(ResolvedKey));

    /// <summary>The facet's selection, or the empty one to toggle from.</summary>
    private RangeSelection Current => Facet.Selection as RangeSelection ?? RangeSelection.Empty;

    /// <summary>Slider handles follow the selection when it is exactly one interval; several intervals or none put them at the ends.</summary>
    private double? SliderFrom => Current.Intervals is [RangeInterval only] ? only.From : null;

    private double? SliderTo => Current.Intervals is [RangeInterval only] ? only.To : null;

    private IReadOnlyList<BucketBar> Bars()
    {
        RangeFacetState facet = Facet;
        var bars = new List<BucketBar>(facet.Buckets.Count + 1);
        foreach (RangeBucket bucket in facet.Buckets)
        {
            RangeBucket captured = bucket;
            bars.Add(new BucketBar(Formatter.FormatRangeBucket(bucket), bucket.TotalCount, bucket.FilteredCount, bucket.Selected, false, () => ClickBucket(captured),
                LinkTo(s => s.ToggleInterval(ResolvedKey, captured.ToInterval()))));
        }

        if (facet.Null.TotalCount > 0)
        {
            bars.Add(new BucketBar(Formatter.NullLabel, facet.Null.TotalCount, facet.Null.FilteredCount, facet.Null.Selected, true, ClickNull,
                LinkTo(s => s.With(ResolvedKey, Current.ToggleNull()))));
        }

        return bars;
    }

    /// <summary>A click toggles the bucket's interval in the facet's set of intervals (concept §5); removing the last part clears the facet.</summary>
    private Task ClickBucket(RangeBucket bucket) => Context.ToggleIntervalAsync(ResolvedKey, bucket.ToInterval());

    /// <summary>The null bar toggles the null rows beside the intervals (concept §4.8).</summary>
    private Task ClickNull() => Context.SelectAsync(ResolvedKey, Current.ToggleNull());

    /// <summary>
    /// Items mode only: defines the facet with these cut points, strictly ascending, as <c>Buckets</c> in the builder: "below the
    /// first", one bucket between each pair and "the last and above" (concept §5, design §9). Watched by content: a change rebuilds.
    /// </summary>
    [Parameter]
    public double[]? Buckets { get; set; }

    /// <summary>Items mode only: about this many buckets on round edges derived from the data, as <c>AutoBuckets</c> in the builder, the default with 10 (concept §5, design §9). Watched: a change rebuilds.</summary>
    [Parameter]
    public int? AutoBuckets { get; set; }

    /// <summary>Items mode only: any other option of the facet's builder. Code, so read when the facet is defined and not watched (design §9).</summary>
    [Parameter]
    public Action<RangeFacetBuilder<T>>? Define { get; set; }

    /// <inheritdoc />
    private protected override bool HasDefinitionParameters => Buckets is not null || AutoBuckets is not null || Define is not null;

    /// <inheritdoc />
    private protected override MarkupDefinition<T> Definition(string key, LambdaExpression selector)
    {
        var parameters = new RangeFacetParameters<T>(Name, Buckets, AutoBuckets, Define);
        return new MarkupDefinition<T>(
            this, ComponentName, IsMetric: false, key, HasDefinitionParameters,
            [selector.ReturnType, Name, Buckets, AutoBuckets],
            b => MarkupFacets.Range(b, key, selector, parameters));
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
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

    /// <summary>
    /// The slider reports null for a side whose handle rests at its end, so a handle at an end leaves that side unbounded
    /// and the open-ended first or last bucket counts as covered (design §9). Both at the ends is no constraint at all.
    /// The slider's interval replaces the facet's selection, including any bars toggled before it.
    /// </summary>
    private Task ApplySlider((double? From, double? To) bounds) =>
        bounds is (null, null) ? Context.ClearAsync(ResolvedKey) : Context.SelectAsync(ResolvedKey, new RangeSelection(bounds.From, bounds.To));
}
