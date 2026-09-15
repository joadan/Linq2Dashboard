using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>A date facet: presets with counts, one bar per calendar period, and the null value beside them (concept §5).</summary>
public partial class DateFacet<T>
{
    private bool collapsed;
    private bool? lastCollapsedParameter;

    /// <summary>The facet key, as defined in the builder. Must be a date facet.</summary>
    [Parameter, EditorRequired]
    public string Key { get; set; } = default!;

    /// <summary>Replaces the default header (title and clear button). Receives the facet state. Collapsing is then controlled only through <see cref="Collapsed"/>.</summary>
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

    /// <summary>Show the total in parentheses after the filtered count, "filtered (total)". On by default; turn off to show the filtered count alone.</summary>
    [Parameter]
    public bool ShowTotals { get; set; } = true;

    /// <summary>Accessible label and tooltip of the header's clear button, which shows an ×.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Shown when the dataset has no value for this facet.</summary>
    [Parameter]
    public string NoValuesText { get; set; } = "No values";

    private DateFacetState Facet => State.Facet(Key) as DateFacetState
        ?? throw new InvalidOperationException($"Facet '{Key}' is not a date facet; use the component for its kind.");

    private IReadOnlyList<BucketBar> Bars()
    {
        DateFacetState facet = Facet;
        var bars = new List<BucketBar>(facet.Buckets.Count + 1);
        foreach (DateBucket bucket in facet.Buckets)
        {
            DateBucket captured = bucket;
            bars.Add(new BucketBar(Formatter.FormatDateBucket(bucket, facet.Granularity), bucket.TotalCount, bucket.FilteredCount, bucket.Selected, false, () => ClickBucket(captured)));
        }

        if (facet.Null.TotalCount > 0)
        {
            bars.Add(new BucketBar(Formatter.NullLabel, facet.Null.TotalCount, facet.Null.FilteredCount, facet.Null.Selected, true, ClickNull));
        }

        return bars;
    }

    /// <summary>A click selects the period; a second click on the selected period clears the facet.</summary>
    private Task ClickBucket(DateBucket bucket)
    {
        DateSelection selection = bucket.ToSelection();
        return selection.Equals(Facet.Selection) ? Context.ClearAsync(Key) : Context.SelectAsync(Key, selection);
    }

    private Task ClickPreset(PresetState preset) =>
        preset.Selected ? Context.ClearAsync(Key) : Context.SelectAsync(Key, preset.ToSelection());

    private Task ClickNull() =>
        Facet.Selection is DateSelection { OnlyNulls: true } ? Context.ClearAsync(Key) : Context.SelectAsync(Key, DateSelection.OnlyNull);

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
}
