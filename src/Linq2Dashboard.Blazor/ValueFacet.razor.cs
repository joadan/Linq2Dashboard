using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>A value or boolean facet: each value with its counts, the null value, the "Other" remainder and an optional search box (concept §5, §6). A click toggles the value (concept §4.1).</summary>
public partial class ValueFacet<T>
{
    private string searchText = string.Empty;
    private bool collapsed;
    private bool? lastCollapsedParameter;

    /// <summary>The facet key, as defined in the builder. Must be a value or boolean facet.</summary>
    [Parameter, EditorRequired]
    public string Key { get; set; } = default!;

    /// <summary>Replaces the default header (title and clear link). Receives the facet state. Collapsing is then controlled only through <see cref="Collapsed"/>.</summary>
    [Parameter]
    public RenderFragment<ValueFacetState>? HeaderTemplate { get; set; }

    /// <summary>Replaces the label and count inside each value's button. The button and its click stay.</summary>
    [Parameter]
    public RenderFragment<FacetValue>? ValueTemplate { get; set; }

    /// <summary>Let the user collapse the facet from its header. On by default; set false for a fixed header.</summary>
    [Parameter]
    public bool Collapsible { get; set; } = true;

    /// <summary>Whether the body is hidden. Bindable: <c>@@bind-Collapsed</c> follows the user's toggling, and setting it applies from the host.</summary>
    [Parameter]
    public bool Collapsed { get; set; }

    /// <summary>Raised when the user toggles the header; the second half of <c>@bind-Collapsed</c>.</summary>
    [Parameter]
    public EventCallback<bool> CollapsedChanged { get; set; }

    /// <summary>Leave out values whose filtered count is zero. Off by default: the core keeps them (concept §4.3) and the UI decides.</summary>
    [Parameter]
    public bool HideZeroCounts { get; set; }

    /// <summary>Show the total in parentheses after the filtered count, "filtered (total)". On by default; turn off to show the filtered count alone.</summary>
    [Parameter]
    public bool ShowTotals { get; set; } = true;

    /// <summary>Maximum number of search results (concept §4.5).</summary>
    [Parameter]
    public int SearchLimit { get; set; } = 20;

    /// <summary>Placeholder of the search box.</summary>
    [Parameter]
    public string SearchPlaceholder { get; set; } = "Search";

    /// <summary>Text of the clear link in the header.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Label of the "Other" row that holds the values Top N left out (concept §6).</summary>
    [Parameter]
    public string OtherText { get; set; } = "Other";

    /// <summary>Shown when a search matches no value.</summary>
    [Parameter]
    public string NoMatchesText { get; set; } = "No matches";

    private ValueFacetState Facet => State.Facet(Key) as ValueFacetState
        ?? throw new InvalidOperationException($"Facet '{Key}' is not a value or boolean facet; use the component for its kind.");

    private bool IsSearching => Facet.IsSearchable && searchText.Length > 0;

    private IEnumerable<FacetValue> Displayed
    {
        get
        {
            IEnumerable<FacetValue> values = IsSearching ? Facet.Search(searchText, SearchLimit) : Facet.Values;
            return HideZeroCounts ? values.Where(v => v.FilteredCount > 0 || v.Selected) : values;
        }
    }

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
