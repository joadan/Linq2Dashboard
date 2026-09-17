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

    /// <summary>Overrides the name given in the builder, which is only a default display name, for example with a localised string (concept §7).</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Replaces the default header (name and clear button). Receives the facet state. Collapsing is then controlled only through <see cref="Collapsed"/>.</summary>
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

    /// <summary>Order of the values on screen (concept §6). Default is the core's rank order, by count. The core still decides which values Top N presents; this only orders them.</summary>
    [Parameter]
    public FacetSort Sort { get; set; } = FacetSort.Rank;

    /// <summary>Reverse the order chosen by <see cref="Sort"/>. For <see cref="FacetSort.Rank"/> that puts the smallest counts first. Null stays last for label and value sorts.</summary>
    [Parameter]
    public bool SortDescending { get; set; }

    /// <summary>Show the total in parentheses after the filtered count, "filtered (total)". On by default; turn off to show the filtered count alone.</summary>
    [Parameter]
    public bool ShowTotals { get; set; } = true;

    /// <summary>Maximum number of search results (concept §4.5).</summary>
    [Parameter]
    public int SearchLimit { get; set; } = 20;

    /// <summary>Placeholder of the search box.</summary>
    [Parameter]
    public string SearchPlaceholder { get; set; } = "Search";

    /// <summary>
    /// Classes for the search box, replacing the library's default look (<c>l2d-input</c>) so a CSS framework's
    /// class takes over cleanly, for example <c>form-control</c>. The hook class <c>l2d-facet-search</c> stays (design §9).
    /// </summary>
    [Parameter]
    public string? InputClass { get; set; }

    /// <summary>Accessible label and tooltip of the header's clear button, which shows an ×.</summary>
    [Parameter]
    public string ClearText { get; set; } = "Clear";

    /// <summary>Label of the "Other" row that holds the values Top N left out (concept §6).</summary>
    [Parameter]
    public string OtherText { get; set; } = "Other";

    /// <summary>Shown when a search matches no value.</summary>
    [Parameter]
    public string NoMatchesText { get; set; } = "No matches";

    private string HeaderName => Name ?? Facet.Name;

    private ValueFacetState Facet => State.Facet(Key) as ValueFacetState
        ?? throw new InvalidOperationException($"Facet '{Key}' is not a value or boolean facet; use the component for its kind.");

    private bool IsSearching => Facet.IsSearchable && searchText.Length > 0;

    private IEnumerable<FacetValue> Displayed
    {
        get
        {
            IEnumerable<FacetValue> values = IsSearching ? Facet.Search(searchText, SearchLimit) : Facet.Values;
            if (HideZeroCounts)
            {
                values = values.Where(v => v.FilteredCount > 0 || v.Selected);
            }

            return Sorted(values);
        }
    }

    /// <summary>Applies <see cref="Sort"/> and <see cref="SortDescending"/>. Label and value sorts keep null last in both directions; ties keep the rank order.</summary>
    private IEnumerable<FacetValue> Sorted(IEnumerable<FacetValue> values)
    {
        if (Sort == FacetSort.Rank)
        {
            return SortDescending ? values.Reverse() : values;
        }

        var list = values.ToList();
        IComparer<FacetValue> comparer = Sort == FacetSort.Value ? Comparer<FacetValue>.Create(CompareValues) : LabelComparer();
        IEnumerable<FacetValue> nonNull = list.Where(v => !v.IsNull);
        IEnumerable<FacetValue> ordered = SortDescending ? nonNull.OrderByDescending(v => v, comparer) : nonNull.OrderBy(v => v, comparer);
        return ordered.Concat(list.Where(v => v.IsNull));
    }

    /// <summary>Orders by the shown text with the formatter's comparer, so the order follows the formatter's culture and not the machine's.</summary>
    private IComparer<FacetValue> LabelComparer()
    {
        ValueFacetState facet = Facet;
        IComparer<string> labels = Formatter.LabelComparer;
        return Comparer<FacetValue>.Create((a, b) => labels.Compare(Formatter.FormatValue(facet, a.Value), Formatter.FormatValue(facet, b.Value)));
    }

    private int CompareValues(FacetValue a, FacetValue b)
    {
        if (a.Value is not IComparable left)
        {
            throw new InvalidOperationException($"Facet '{Key}' cannot be sorted by value: {a.Value?.GetType().Name} does not implement IComparable. Use FacetSort.Label instead.");
        }

        return left.CompareTo(b.Value);
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
