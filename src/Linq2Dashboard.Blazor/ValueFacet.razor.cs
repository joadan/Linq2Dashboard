using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// A value, boolean or multi-valued facet: each value with its counts, the null value, the "Other" remainder and an
/// optional search box (concept §5, §6). A click toggles the value (concept §4.1). A multi-valued facet renders the
/// same way, with the class <c>l2d-multi-value-facet</c> on the root and never an "Other" row, since its counts overlap.
/// A facet with groups (concept §6) lists each group as a row that folds and selects all its values, then the ungrouped
/// values and the null value.
/// </summary>
public partial class ValueFacet<T>
{
    private string searchText = string.Empty;

    /// <summary>Stands in for null in <c>@key</c>, which needs an object; the null value is one list item like any other (concept §4.8).</summary>
    private static readonly object NullKey = new();

    private bool collapsed;
    private bool? lastCollapsedParameter;

    /// <summary>Groups the user folded or unfolded against <see cref="GroupsCollapsed"/>. UI state like the search text, never a selection.</summary>
    private readonly HashSet<string> foldToggled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A group's <c>@key</c>, so a group never shares one with a value of the same text.</summary>
    private readonly record struct GroupKey(string Name);

    /// <summary>
    /// Overrides the name given in the builder, which is only a default display name, for example with a localised string
    /// (concept §7). When this component defines the facet under a view with <c>Items</c>, it is the facet's name (design §9).
    /// </summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Items mode only: defines the facet with at most this many values plus "Other", as <c>Top</c> in the builder (concept §6, design §9). Watched: a change rebuilds.</summary>
    [Parameter]
    public int? Top { get; set; }

    /// <summary>Items mode only: which count picks the top values, as <c>RankBy</c> in the builder (concept §6, design §9). Watched: a change rebuilds.</summary>
    [Parameter]
    public RankMode? RankBy { get; set; }

    /// <summary>Items mode only: whether the facet has a search box, as <c>Searchable</c> in the builder (concept §6, design §9). Watched: a change rebuilds.</summary>
    [Parameter]
    public bool? Searchable { get; set; }

    /// <summary>
    /// Items mode only: the text shown and searched for each value, read from the first row that has it, as <c>Label</c> in the
    /// builder (concept §5, design §9). Code, so read when the facet is defined and not watched; a multi-valued facet sets its
    /// label in <see cref="Define"/>, since it reads the value rather than the row.
    /// </summary>
    [Parameter]
    public Func<T, string?>? Label { get; set; }

    /// <summary>
    /// Items mode only: a named group per value, read from the first row that has it, as <c>GroupBy</c> in the builder
    /// (concept §6, design §9), for example <c>GroupBy="o =&gt; o.Market"</c>. Code, so read when the facet is defined and not
    /// watched. Not with <see cref="Top"/> or <see cref="Multiple"/>.
    /// </summary>
    [Parameter]
    public Func<T, string?>? GroupBy { get; set; }

    /// <summary>Start every group of a grouped facet folded, showing only the group rows. Off by default: groups start open (design §9).</summary>
    [Parameter]
    public bool GroupsCollapsed { get; set; }

    /// <summary>
    /// Items mode only: the selector returns a collection and the facet counts a row under each of its values, as
    /// <c>MultiValueFacet</c> in the builder (concept §5, design §9). Watched: a change rebuilds.
    /// </summary>
    [Parameter]
    public bool Multiple { get; set; }

    /// <summary>
    /// Items mode only: any other option of the facet's builder, for example <c>Comparer</c> or <c>Serialize</c>. The component does
    /// not know the value type, so the lambda names the builder type itself, <c>Define="(ValueFacetBuilder&lt;Order, string&gt; f) =&gt;
    /// f.Comparer(StringComparer.Ordinal)"</c>, or <c>MultiValueFacetBuilder</c> with <see cref="Multiple"/>; a builder type that does
    /// not match the selector throws when the facet is defined. Code, so read when the facet is defined and not watched (design §9).
    /// </summary>
    [Parameter]
    public Delegate? Define { get; set; }

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

    /// <summary>Show the total in parentheses after the filtered count, "filtered (total)". Off by default: the two numbers are equal until another facet narrows the set, and the tooltip always carries both (design §9).</summary>
    [Parameter]
    public bool ShowTotals { get; set; }

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

    private ValueFacetState Facet => State.Facet(ResolvedKey) as ValueFacetState
        ?? throw WrongKind(State.Facet(ResolvedKey));

    private bool IsSearching => Facet.IsSearchable && searchText.Length > 0;

    /// <summary>The facet has groups and the list shows them; a search shows its matches flat, each with its group (design §9).</summary>
    private bool IsGrouped => Facet.Groups.Count > 0 && !IsSearching;

    /// <summary>
    /// The values listed outside any group: every value when the list is flat; under groups, the ungrouped ones followed by
    /// the null value, which is never in a group (concept §6).
    /// </summary>
    private IEnumerable<FacetValue> Displayed
    {
        get
        {
            if (IsSearching)
            {
                return Sorted(Shown(Facet.Search(searchText, SearchLimit)));
            }

            if (!IsGrouped)
            {
                return Sorted(Shown(Facet.Values));
            }

            var grouped = new HashSet<FacetValue>(Facet.Groups.SelectMany(g => g.Values), ReferenceEqualityComparer.Instance);
            IEnumerable<FacetValue> ungrouped = Shown(Facet.Values.Where(v => !grouped.Contains(v)));
            return Sorted(ungrouped.Where(v => !v.IsNull)).Concat(ungrouped.Where(v => v.IsNull));
        }
    }

    /// <summary>
    /// The groups in the order <see cref="Sort"/> asks for, rank or name, each with its shown values in the same order. A group
    /// whose values <see cref="HideZeroCounts"/> hides entirely is left out.
    /// </summary>
    private IEnumerable<(ValueGroup Group, IReadOnlyList<FacetValue> Values)> DisplayedGroups
    {
        get
        {
            IEnumerable<ValueGroup> groups = Facet.Groups;
            if (Sort == FacetSort.Rank)
            {
                groups = SortDescending ? groups.Reverse() : groups;
            }
            else
            {
                IComparer<string> names = Formatter.LabelComparer;
                groups = SortDescending ? groups.OrderByDescending(g => g.Name, names) : groups.OrderBy(g => g.Name, names);
            }

            foreach (ValueGroup group in groups)
            {
                var values = Sorted(Shown(group.Values)).ToList();
                if (values.Count > 0)
                {
                    yield return (group, values);
                }
            }
        }
    }

    /// <summary>Applies <see cref="HideZeroCounts"/>; a selected value always stays.</summary>
    private IEnumerable<FacetValue> Shown(IEnumerable<FacetValue> values) =>
        HideZeroCounts ? values.Where(v => v.FilteredCount > 0 || v.Selected) : values;

    /// <summary>Whether a group shows only its row. Never when clicks are links: nothing could unfold it there (design §9).</summary>
    private bool IsFolded(string group) => !Context.Links && GroupsCollapsed != foldToggled.Contains(group);

    private void ToggleFolded(string group)
    {
        if (!foldToggled.Add(group))
        {
            foldToggled.Remove(group);
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
            throw new InvalidOperationException($"Facet '{ResolvedKey}' cannot be sorted by value: {a.Value?.GetType().Name} does not implement IComparable. Use FacetSort.Label instead.");
        }

        return left.CompareTo(b.Value);
    }

    /// <inheritdoc />
    private protected override bool HasDefinitionParameters =>
        Top is not null || RankBy is not null || Searchable is not null || Label is not null || GroupBy is not null || Multiple || Define is not null;

    /// <inheritdoc />
    private protected override MarkupDefinition<T> Definition(string key, LambdaExpression selector)
    {
        var parameters = new ValueFacetParameters<T>(Name, Top, RankBy, Searchable, Label, GroupBy, Multiple, Define);
        return new MarkupDefinition<T>(
            this, ComponentName, IsMetric: false, key, HasDefinitionParameters,
            [selector.ReturnType, Name, Top, RankBy, Searchable, Multiple],
            b => MarkupFacets.Value(b, key, selector, parameters));
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
}
