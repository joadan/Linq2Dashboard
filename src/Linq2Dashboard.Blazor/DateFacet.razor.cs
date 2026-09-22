using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>A date facet: presets with counts, one bar per calendar period, and the null value beside them (concept §5). A bar or preset click toggles that part, so they select like values.</summary>
public partial class DateFacet<T>
{
    private bool collapsed;
    private bool? lastCollapsedParameter;


    /// <summary>Overrides the name given in the builder, which is only a default display name, for example with a localised string (concept §7).</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Replaces the default header (name and clear button). Receives the facet state. Collapsing is then controlled only through <see cref="Collapsed"/>.</summary>
    [Parameter]
    public RenderFragment<DateFacetState>? HeaderTemplate { get; set; }

    /// <summary>Let the user collapse the facet from its header. On by default; set false for a fixed header.</summary>
    [Parameter]
    public bool Collapsible { get; set; } = true;

    /// <summary>Whether the body is hidden. Bindable: <c>@@bind-Collapsed</c> follows the user's toggling, and setting it applies from the host.</summary>
    [Parameter]
    public bool Collapsed { get; set; }

    /// <summary>Raised when the user toggles the header; the second half of <c>@bind-Collapsed</c>.</summary>
    [Parameter]
    public EventCallback<bool> CollapsedChanged { get; set; }

    /// <summary>Vertical bars per period with labels beneath, or rows with an inline bar.</summary>
    [Parameter]
    public BucketLayout Layout { get; set; } = BucketLayout.Histogram;

    /// <summary>Show the configured presets with their counts above the periods (concept §5).</summary>
    [Parameter]
    public bool ShowPresets { get; set; } = true;

    /// <summary>Show the count on each period and preset. Default true.</summary>
    [Parameter]
    public bool ShowCounts { get; set; } = true;

    /// <summary>Show the total in parentheses after the filtered count, "filtered (total)". Off by default: the two numbers are equal until another facet narrows the set, and the tooltip always carries both (design §9).</summary>
    [Parameter]
    public bool ShowTotals { get; set; }

    /// <summary>Accessible label and tooltip of the header's clear button, which shows an ×.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Shown when the dataset has no value for this facet.</summary>
    [Parameter]
    public string NoValuesText { get; set; } = "No values";

    private string HeaderName => Name ?? Facet.Name;

    private DateFacetState Facet => State.Facet(ResolvedKey) as DateFacetState
        ?? throw WrongKind(State.Facet(ResolvedKey));

    /// <summary>The facet's selection, or the empty one to toggle from.</summary>
    private DateSelection Current => Facet.Selection as DateSelection ?? DateSelection.Empty;

    private IReadOnlyList<BucketBar> Bars()
    {
        DateFacetState facet = Facet;
        var bars = new List<BucketBar>(facet.Buckets.Count + 1);
        foreach (DateBucket bucket in facet.Buckets)
        {
            DateBucket captured = bucket;
            bars.Add(new BucketBar(Formatter.FormatDateBucket(bucket, facet.Granularity), bucket.TotalCount, bucket.FilteredCount, bucket.Selected, false, () => ClickBucket(captured),
                LinkTo(s => s.ToggleInterval(ResolvedKey, captured.ToInterval()))));
        }

        if (facet.Null.TotalCount > 0)
        {
            bars.Add(new BucketBar(Formatter.NullLabel, facet.Null.TotalCount, facet.Null.FilteredCount, facet.Null.Selected, true, ClickNull,
                LinkTo(s => s.With(ResolvedKey, Current.ToggleNull()))));
        }

        return bars;
    }

    /// <summary>A click toggles the period in the facet's set of parts (concept §5); removing the last part clears the facet.</summary>
    private Task ClickBucket(DateBucket bucket) => Context.ToggleIntervalAsync(ResolvedKey, bucket.ToInterval());

    /// <summary>A click toggles the relative preset as a part of its own, beside any periods.</summary>
    private Task ClickPreset(PresetState preset) => Context.ToggleIntervalAsync(ResolvedKey, preset.ToInterval());

    /// <summary>The null bar toggles the null rows beside the parts (concept §4.8).</summary>
    private Task ClickNull() => Context.SelectAsync(ResolvedKey, Current.ToggleNull());

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
}
