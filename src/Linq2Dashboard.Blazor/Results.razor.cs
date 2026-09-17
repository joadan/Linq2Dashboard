using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;

namespace Linq2Dashboard.Blazor;

/// <summary>The matching rows in the application-defined order (concept §3), paged or virtualised, as a list or a table. The row markup is the host's, through <see cref="RowTemplate"/>.</summary>
public partial class Results<T>
{
    private int pageIndex;
    private Selections? pageSelections;

    /// <summary>Renders one matching row. Required: the core cannot know what a row looks like (design §9).</summary>
    [Parameter, EditorRequired]
    public RenderFragment<T> RowTemplate { get; set; } = default!;

    /// <summary>Rendered once before the rows. In <see cref="ResultsLayout.Table"/> it goes inside <c>thead</c> and should render a <c>tr</c>.</summary>
    [Parameter]
    public RenderFragment? HeaderTemplate { get; set; }

    /// <summary>Rendered when no row matches. Defaults to <see cref="EmptyText"/>.</summary>
    [Parameter]
    public RenderFragment? EmptyTemplate { get; set; }

    /// <summary>A plain list of row fragments, or a table with the rows inside <c>tbody</c>.</summary>
    [Parameter]
    public ResultsLayout Layout { get; set; } = ResultsLayout.List;

    /// <summary>
    /// Scroll through every matching row in a fixed-height container instead of paging, rendering
    /// only the visible rows through Blazor's <c>Virtualize</c>. Rows must have a roughly constant
    /// height, given by <see cref="ItemSize"/>. The container height is <c>--l2d-results-height</c>.
    /// </summary>
    [Parameter]
    public bool Virtualize { get; set; }

    /// <summary>Row height in pixels for virtualisation. Default 32.</summary>
    [Parameter]
    public float ItemSize { get; set; } = 32f;

    /// <summary>Rows rendered beyond the visible area for virtualisation. Default 5.</summary>
    [Parameter]
    public int OverscanCount { get; set; } = 5;

    /// <summary>Rows per page when paging. Default 50.</summary>
    [Parameter]
    public int PageSize { get; set; } = 50;

    /// <summary>The page the component starts on and, through the callback, the one it is on.</summary>
    [Parameter]
    public int PageIndex { get; set; }

    /// <summary>Raised when the user changes page; the second half of <c>@bind-PageIndex</c>.</summary>
    [Parameter]
    public EventCallback<int> PageIndexChanged { get; set; }

    /// <summary>Show the summary line above the rows. Default true.</summary>
    [Parameter]
    public bool ShowSummary { get; set; } = true;

    /// <summary>Format for the summary line when paging; {0} first row, {1} last row, {2} matching count.</summary>
    [Parameter]
    public string SummaryFormat { get; set; } = "Showing {0}–{1} of {2}";

    /// <summary>Format for the summary line when virtualised; {0} matching count.</summary>
    [Parameter]
    public string VirtualSummaryFormat { get; set; } = "{0} rows";

    /// <summary>Format for the pager status; {0} current page, {1} page count.</summary>
    [Parameter]
    public string PageFormat { get; set; } = "Page {0} of {1}";

    /// <summary>Shown when no row matches and there is no <see cref="EmptyTemplate"/>.</summary>
    [Parameter]
    public string EmptyText { get; set; } = "No matching rows";

    /// <summary>Accessible label of the pager navigation.</summary>
    [Parameter]
    public string PagerLabel { get; set; } = "Pages";

    /// <summary>Accessible label and tooltip of the first-page button, which shows «.</summary>
    [Parameter]
    public string FirstPageText { get; set; } = "First page";

    /// <summary>Accessible label and tooltip of the previous-page button, which shows ‹.</summary>
    [Parameter]
    public string PreviousPageText { get; set; } = "Previous page";

    /// <summary>Accessible label and tooltip of the next-page button, which shows ›.</summary>
    [Parameter]
    public string NextPageText { get; set; } = "Next page";

    /// <summary>Accessible label and tooltip of the last-page button, which shows ».</summary>
    [Parameter]
    public string LastPageText { get; set; } = "Last page";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        ArgumentNullException.ThrowIfNull(RowTemplate);
        ArgumentOutOfRangeException.ThrowIfLessThan(PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(PageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ItemSize);
        ArgumentOutOfRangeException.ThrowIfNegative(OverscanCount);
        pageIndex = PageIndex;
    }

    /// <summary>
    /// A new state means new matching rows: go back to the first page (design §9). The virtualised
    /// list is keyed on the selections, so a new selection recreates it and it queries afresh from the top.
    /// </summary>
    protected override void OnDashboardStateChanged()
    {
        if (!State.Selections.Equals(pageSelections))
        {
            pageIndex = 0;
        }
    }

    private ValueTask<ItemsProviderResult<T>> ProvideItems(ItemsProviderRequest request)
    {
        pageSelections = State.Selections;
        IReadOnlyList<T> items = State.GetItems(request.StartIndex, request.Count);
        return ValueTask.FromResult(new ItemsProviderResult<T>(items, State.MatchingCount));
    }

    private ResultPage<T> CurrentPage()
    {
        pageSelections = State.Selections;
        int last = Math.Max(0, State.PageCount(PageSize) - 1);
        pageIndex = Math.Clamp(pageIndex, 0, last);
        return State.GetPage(pageIndex, PageSize);
    }

    private Task GoTo(int index)
    {
        pageIndex = Math.Max(0, index);
        return PageIndexChanged.InvokeAsync(pageIndex);
    }
}
